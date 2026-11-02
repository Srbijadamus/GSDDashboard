# notify_restart.py
# Jednokratno slanje "Pulse server restartovan" preko Teams/Power Automate webhooka posle podizanja sistema.
# URL webhooka se cita iz C:\PulseOps\notify_webhook.url (tajna - nikad se ne ispisuje).
# U C:\PulseOps\notify_restart.log upisuje SAMO vreme, broj pokusaja i ishod (OK ili tip greske + HTTP status).

import ctypes
import json
import socket
import sys
import time
import urllib.error
import urllib.request
from datetime import datetime, timezone

URL_PATH      = "C:/PulseOps/notify_webhook.url"
LOG_PATH      = "C:/PulseOps/notify_restart.log"
MAX_ATTEMPTS  = 10
RETRY_SECONDS = 180   # 3 min razmaka izmedju pokusaja -> ukupno ~30 min

_GetTickCount64 = ctypes.windll.kernel32.GetTickCount64
_GetTickCount64.restype = ctypes.c_uint64
_GetTickCount64.argtypes = []


def log(msg):
    line = "%s %s" % (datetime.now(timezone.utc).strftime("%Y-%m-%d %H:%M:%S UTC"), msg)
    try:
        with open(LOG_PATH, "a", encoding="utf-8") as f:
            f.write(line + "\n")
    except Exception:
        pass


def last_boot_time():
    # Vreme poslednjeg pokretanja = sada - uptime (GetTickCount64 = ms od boot-a).
    uptime_ms = _GetTickCount64()
    boot_epoch = time.time() - (uptime_ms / 1000.0)
    return datetime.fromtimestamp(boot_epoch, timezone.utc)


def read_webhook_url():
    with open(URL_PATH, "r", encoding="utf-8") as f:
        return f.read().strip()


def build_payload(boot, now, host):
    card = {
        "type": "AdaptiveCard",
        "version": "1.4",
        "body": [
            {"type": "TextBlock", "size": "Medium", "weight": "Bolder",
             "text": "Pulse server restartovan"},
            {"type": "TextBlock", "wrap": True,
             "text": "Poslednje pokretanje OS-a (UTC): %s" % boot.strftime("%Y-%m-%d %H:%M:%S UTC")},
            {"type": "TextBlock", "wrap": True,
             "text": "Vreme slanja: %s" % now.strftime("%Y-%m-%d %H:%M:%S UTC")},
            {"type": "TextBlock", "wrap": True,
             "text": "Računar: %s" % host},
            {"type": "TextBlock", "wrap": True,
             "text": "Tuneli, kiosk i health check čekaju prijavu korisnika S69307. Prijavi se na cloud PC."},
        ],
    }
    return {
        "type": "message",
        "attachments": [
            {"contentType": "application/vnd.microsoft.card.adaptive", "content": card}
        ],
    }


def main():
    try:
        url = read_webhook_url()
    except Exception as e:
        log("ERROR: %s" % type(e).__name__)
        return 1
    if not url:
        log("ERROR: EmptyWebhookUrl")
        return 1

    boot = last_boot_time()
    host = socket.gethostname()

    for attempt in range(1, MAX_ATTEMPTS + 1):
        try:
            now = datetime.now(timezone.utc)
            payload = build_payload(boot, now, host)
            data = json.dumps(payload).encode("utf-8")
            req = urllib.request.Request(url, data=data, headers={"Content-Type": "application/json"})
            with urllib.request.urlopen(req, timeout=15) as resp:
                status = resp.getcode()
            if 200 <= status < 300:
                log("pokušaj %d/%d OK" % (attempt, MAX_ATTEMPTS))
                return 0
            log("pokušaj %d/%d ERROR: HTTP %d" % (attempt, MAX_ATTEMPTS, status))
        except urllib.error.HTTPError as e:
            log("pokušaj %d/%d ERROR: HTTP %d" % (attempt, MAX_ATTEMPTS, e.code))
        except Exception as e:
            log("pokušaj %d/%d ERROR: %s" % (attempt, MAX_ATTEMPTS, type(e).__name__))
        if attempt < MAX_ATTEMPTS:
            time.sleep(RETRY_SECONDS)

    return 1


if __name__ == "__main__":
    sys.exit(main())
