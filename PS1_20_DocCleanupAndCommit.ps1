$doc = "C:\GSDDashboard\documentation"

Remove-Item "$doc\handoff_prompt1.md" -Force -ErrorAction SilentlyContinue
Remove-Item "$doc\handoff_prompt2.md" -Force -ErrorAction SilentlyContinue
Remove-Item "$doc\handoff_prompt3.md" -Force -ErrorAction SilentlyContinue
Remove-Item "$doc\handoff_prompt4.md" -Force -ErrorAction SilentlyContinue

Write-Host "Old handoff files deleted."

Set-Location "C:\GSDDashboard"
git add documentation/
git commit -m "2026-06-16: Update documentation to current state"
git push

Write-Host "Done."
