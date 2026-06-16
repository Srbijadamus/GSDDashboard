"""
geocoder.py — Backfill WicLocations.Coordinates using Nominatim (geopy).
Requirement: pip install geopy pyodbc
Rate-limit: 1 req/s (Nominatim ToS).
Failure policy: leave NULL + log — NEVER fabricate coordinates.
"""
import pyodbc, time, re, sys
from typing import Optional

try:
    from geopy.geocoders import Nominatim
    from geopy.exc import GeocoderTimedOut, GeocoderServiceError
except ImportError:
    print("ERROR: geopy not installed. Run: pip install geopy")
    sys.exit(1)

CONN_STR = (
    "DRIVER={ODBC Driver 17 for SQL Server};"
    "SERVER=localhost\\SQLEXPRESS;"
    "DATABASE=GSDDashboard;"
    "Trusted_Connection=yes;"
    "TrustServerCertificate=yes;"
)
USER_AGENT = "gsd-dashboard-geocoder/1.0"

geo = Nominatim(user_agent=USER_AGENT)


def parse_location(code: str, city: Optional[str], postal: Optional[str], country: Optional[str]):
    """Return (query, confidence_note) for geocoding."""
    # New format: DE~86150~Augsburg~Augsburger Str 1
    if "~" in code:
        parts = code.split("~")
        c = country or (parts[0] if parts[0] else "DE")
        plz = postal or (parts[1].strip() if len(parts) > 1 else "")
        cty = city or (parts[2].strip() if len(parts) > 2 else "")
        adr = parts[3].strip() if len(parts) > 3 else ""
        # Full address first, fall back to city+PLZ
        if adr and cty and plz:
            return f"{adr}, {plz} {cty}, {c}", "address"
        if cty and plz:
            return f"{plz} {cty}, {c}", "postal+city"
        if cty:
            return f"{cty}, {c}", "city-only"
    # Old format: DE_Berlin_Gauss, RENDSBURG, NL_Zwolle, DE_Essen_BP1 …
    if city:
        c = country or "DE"
        return f"{city}, {c}", "city-only"
    # Last resort: strip prefixes and underscores from code
    name = re.sub(r"^(DE|NL)[-_]?", "", code).replace("_", " ").strip()
    return f"{name}, {country or 'DE'}", "city-only"


def geocode_one(query: str, retries: int = 2):
    for attempt in range(retries + 1):
        try:
            loc = geo.geocode(query, language="de", timeout=10)
            return loc
        except GeocoderTimedOut:
            if attempt < retries:
                time.sleep(2)
        except GeocoderServiceError as e:
            print(f"  Service error: {e}")
            return None
    return None


def main():
    conn = pyodbc.connect(CONN_STR)
    cur = conn.cursor()

    cur.execute("""
        SELECT Id, LocationCode, City, PostalCode, Country, Coordinates
        FROM WicLocations
        WHERE IsActive = 1
        ORDER BY LocationCode
    """)
    rows = cur.fetchall()

    total = len(rows)
    success = 0
    city_only = 0
    failed = 0

    print(f"Found {total} active WIC locations.")

    for row in rows:
        loc_id, code, city, postal, country, existing_coords = row

        if existing_coords:
            print(f"[SKIP] {code} — already has coordinates: {existing_coords}")
            success += 1
            continue

        query, confidence = parse_location(code, city, postal, country)
        print(f"[{code}] geocoding: '{query}' ({confidence}) ...", end=" ", flush=True)

        time.sleep(1.05)  # Nominatim rate limit: 1 req/s
        result = geocode_one(query)

        if result is None and confidence != "city-only" and city:
            # Retry with city only
            fallback_query = f"{city}, {country or 'DE'}"
            print(f"  >> retry city-only: '{fallback_query}' ...", end=" ", flush=True)
            time.sleep(1.05)
            result = geocode_one(fallback_query)
            if result:
                confidence = "city-only"

        if result:
            coords = f"{result.latitude:.6f},{result.longitude:.6f}"
            cur.execute(
                "UPDATE WicLocations SET Coordinates = ? WHERE Id = ?",
                coords, loc_id
            )
            conn.commit()
            marker = "OK" if confidence == "address" else "~"
            print(f"{marker} {coords}  [{confidence}]  raw: {result.address[:60]}")
            success += 1
            if confidence == "city-only":
                city_only += 1
        else:
            print("FAIL — leaving NULL")
            failed += 1

    print(f"\n=== Results ===")
    print(f"Total:      {total}")
    print(f"Success:    {success}  (of which city-only precision: {city_only})")
    print(f"Failed:     {failed}")
    print(f"Success%:   {success/total*100:.0f}%")

    # Sanity check: Berlin → Munich
    cur.execute("SELECT Coordinates FROM WicLocations WHERE City IN ('Berlin','München','Munich','Munchen') AND Coordinates IS NOT NULL")
    berlin_r = cur.execute("SELECT TOP 1 Coordinates FROM WicLocations WHERE City LIKE '%Berlin%' AND Coordinates IS NOT NULL").fetchone()
    munich_r = cur.execute("SELECT TOP 1 Coordinates FROM WicLocations WHERE City LIKE '%M%nchen%' AND Coordinates IS NOT NULL").fetchone()
    if berlin_r and munich_r:
        import math
        def haver(a, b):
            lat1,lon1 = map(float, a.split(","))
            lat2,lon2 = map(float, b.split(","))
            R = 6371
            dlat = math.radians(lat2-lat1); dlon = math.radians(lon2-lon1)
            h = math.sin(dlat/2)**2 + math.cos(math.radians(lat1))*math.cos(math.radians(lat2))*math.sin(dlon/2)**2
            return R*2*math.atan2(math.sqrt(h), math.sqrt(1-h))
        km = haver(berlin_r[0], munich_r[0])
        print(f"\nSanity check Berlin->Munich: {km:.0f} km (expected ~504 km)")
        if abs(km - 504) < 100:
            print("Sanity check PASSED")
        else:
            print("WARNING: sanity check outside expected range -- verify coordinates manually.")

    conn.close()


if __name__ == "__main__":
    main()
