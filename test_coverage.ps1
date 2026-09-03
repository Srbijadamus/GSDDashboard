#Requires -Version 7.0
$url = 'https://d2jn94qg-5000.euw.devtunnels.ms/api/assistant/ask'
# Build location names using char codes to avoid encoding issues:
# u-umlaut=252, o-umlaut=246, a-umlaut=228, sharp-s=223
$ue = [char]252; $oe = [char]246; $ae = [char]228; $ss = [char]223
$qs = @(
    "Cover M${ue}nchen",
    "Cover Saarbr${ue}cken",
    "Cover F${ue}rstenwalde",
    "Cover Osnabr${ue}ck",
    "Cover M${ue}lheim",
    "Cover M${ue}nster",
    "Cover Berlin - Gau${ss}str.",
    "Cover Berlin - Br${ue}ckenstrasse",
    "Wer deckt Demmin ab?",
    "Wer kann M${ue}nchen abdecken?",
    "M${ue}nchen Abdeckung",
    "Demmin",
    "Hamburg",
    "Saffig"
)
foreach ($q in $qs) {
    $body = "{`"question`":`"$q`"}"
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($body)
    try {
        $r = Invoke-RestMethod $url -Method Post -ContentType 'application/json; charset=utf-8' -Body $bytes -TimeoutSec 10
        $ans = if ($r.answerText.Length -gt 75) { $r.answerText.Substring(0,75) + '...' } else { $r.answerText }
        $rows = if ($r.table) { $r.table.Count } else { 0 }
        Write-Host ("$q -> $ans [rows=$rows]")
    } catch {
        Write-Host ("$q -> ERROR: " + $_.Exception.Message)
    }
}
