// Shared employee name resolver — used by BulkRtm and Vacations Bulk Import.
// Handles umlauts/diacritics, order-insensitive token matching, and per-token fuzzy matching.

export interface EmployeeBase {
  employeeId: string
  fullName: string | null
}

export type MatchType = "exact" | "token" | "token-fuzzy" | "fuzzy"

export type BaseRowStatus =
  | "resolved"           // exact normalized full-string match
  | "resolved-corrected" // order-swap, per-token fuzzy, or whole-string fuzzy auto-match
  | "suggest"            // close candidate(s) found — user must pick
  | "ambiguous"          // multiple equally-exact matches
  | "unresolved"         // no candidate found

export interface ResolveResult<T extends EmployeeBase = EmployeeBase> {
  resolved: T | null
  ambiguous: T[]
  status: BaseRowStatus
  matchType?: MatchType
  suggestions?: T[]
}

export function normStr(s: string): string {
  return (s ?? "")
    .toLowerCase()
    .replace(/ß/g, "ss")
    .normalize("NFD")
    .replace(/[̀-ͯ]/g, "")
    .replace(/[-]/g, " ")
    .replace(/\s+/g, " ")
    .trim()
}

// Damerau-Levenshtein: like Levenshtein but treats adjacent transpositions (ei↔ie) as 1 edit.
export function damerauLevenshtein(a: string, b: string): number {
  const m = a.length, n = b.length
  if (m === 0) return n
  if (n === 0) return m
  const d: number[][] = Array.from({ length: m + 1 }, (_, i) =>
    Array.from({ length: n + 1 }, (_, j) => (i === 0 ? j : j === 0 ? i : 0))
  )
  for (let i = 1; i <= m; i++) {
    for (let j = 1; j <= n; j++) {
      const cost = a[i - 1] === b[j - 1] ? 0 : 1
      d[i][j] = Math.min(
        d[i - 1][j] + 1,
        d[i][j - 1] + 1,
        d[i - 1][j - 1] + cost
      )
      // transposition
      if (i > 1 && j > 1 && a[i - 1] === b[j - 2] && a[i - 2] === b[j - 1]) {
        d[i][j] = Math.min(d[i][j], d[i - 2][j - 2] + cost)
      }
    }
  }
  return d[m][n]
}

function tokenThresh(t: string): number {
  return t.length < 6 ? 1 : 2
}

// Resolve `rawName` against `employees`.
//
// Matching pipeline (first match wins):
//   1. Exact normalized full-string match            → "resolved"
//   2. Order-insensitive exact token match           → "resolved-corrected"  (handles Lastname Firstname)
//   3. Per-token Damerau-Levenshtein match           → "resolved-corrected"  (handles single-token typos)
//   4. Whole-string Damerau-Levenshtein (autoThresh) → "resolved-corrected"
//   5. Within suggestThresh                          → "suggest" (top 1–3)
//   6. Nothing                                       → "unresolved"
export function resolveEmployee<T extends EmployeeBase>(
  rawName: string,
  employees: T[]
): ResolveResult<T> {
  const q     = normStr(rawName)
  const typed = q.split(" ").filter(w => w.length > 1)

  // ── 1. Exact normalized full-string ────────────────────────────────────────
  const exact = employees.filter(e => normStr(e.fullName ?? "") === q)
  if (exact.length === 1) return { resolved: exact[0], ambiguous: [], status: "resolved",  matchType: "exact" }
  if (exact.length > 1)  return { resolved: null, ambiguous: exact, status: "ambiguous" }

  // ── 2. Order-insensitive exact token set ───────────────────────────────────
  // "Scheifele Lukas" typed → tokens {scheifele, lukas}
  // stored "Lukas Scheifele" → stored tokens {lukas, scheifele} → both present → match
  if (typed.length >= 2) {
    const tokenExact = employees.filter(e => {
      const stored = normStr(e.fullName ?? "").split(" ").filter(w => w.length > 1)
      return typed.every(t => stored.includes(t))
    })
    if (tokenExact.length === 1) return { resolved: tokenExact[0], ambiguous: [], status: "resolved-corrected", matchType: "token" }
    if (tokenExact.length > 1)  return { resolved: null, ambiguous: tokenExact, status: "ambiguous" }
  }

  // ── 3. Per-token Damerau-Levenshtein ───────────────────────────────────────
  // "Scheifele Lukas" typed  →  typed tokens: [scheifele, lukas]
  // stored "Lukas Schiefele" →  stored tokens: [lukas, schiefele]
  // damerau("scheifele","schiefele") = 1 (ei↔ie transposition) ≤ thresh(9)=2 → match
  if (typed.length >= 2) {
    const tfm = employees
      .map(e => {
        const stored = normStr(e.fullName ?? "").split(" ").filter(w => w.length > 1)
        const allMatch = typed.every(t =>
          stored.some(s => damerauLevenshtein(t, s) <= tokenThresh(t))
        )
        if (!allMatch) return null
        const score = typed.reduce(
          (sum, t) => sum + Math.min(...stored.map(s => damerauLevenshtein(t, s))), 0
        )
        return { emp: e, score }
      })
      .filter((x): x is { emp: T; score: number } => x !== null)

    if (tfm.length > 0) {
      tfm.sort((a, b) => a.score - b.score)
      const best = tfm[0].score
      const tied = tfm.filter(x => x.score === best)
      if (tied.length === 1)
        return { resolved: tied[0].emp, ambiguous: [], status: "resolved-corrected", matchType: "token-fuzzy" }
      return { resolved: null, ambiguous: [], status: "suggest", suggestions: tfm.slice(0, 3).map(x => x.emp) }
    }
  }

  // ── 4 & 5. Whole-string Damerau-Levenshtein ────────────────────────────────
  const autoThresh    = q.length < 6 ? 1 : 2
  const suggestThresh = q.length < 6 ? 2 : 4

  const scored = employees
    .map(e => ({ emp: e, dist: damerauLevenshtein(q, normStr(e.fullName ?? "")) }))
    .filter(x => x.dist <= suggestThresh)
    .sort((a, b) => a.dist - b.dist)

  if (scored.length === 0) return { resolved: null, ambiguous: [], status: "unresolved" }

  const best    = scored[0].dist
  const topTied = scored.filter(x => x.dist === best)

  if (best <= autoThresh && topTied.length === 1)
    return { resolved: topTied[0].emp, ambiguous: [], status: "resolved-corrected", matchType: "fuzzy" }

  return { resolved: null, ambiguous: [], status: "suggest", suggestions: scored.slice(0, 3).map(x => x.emp) }
}
