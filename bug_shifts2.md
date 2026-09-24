Excellent diagnosis - you proved the cause instead of guessing, and you ruled out all three
of my suspects with evidence. Agreed on the root cause: backend emits "date", frontend reads
"shiftDate".

One concern before you fix it. My instinct says the frontend is the wrong side of this
mismatch - "date" is a perfectly normal field name and the backend is not wrong. Renaming a
DTO field changes the shape of an API response, which is the riskier of the two edits.

Your counter-argument is strong though: /timeline has exactly one consumer, and the backend
fix avoids a frontend rebuild. So I accept your plan, with one condition:

CONFIRM FIRST, in writing, that GET /api/employees/{id}/timeline is consumed by nothing else -
not the kiosk, not any PS1 script, not any other page, not the deployed bundle beyond
AgentHome. If anything else reads "date", fix the frontend instead.

Then proceed with your steps 1-4. Requirements:
- prove it with the real API response for 9074341 showing "shiftDate"
- prove the frontend filter now yields 73 future rows, first one on or after 2026-09-21
- rebuild and restart the LOCAL backend only, do not deploy, do not touch the enforcement flag
- do not touch C:\HealthCheck

Separately, once this works, I want the agent card to also show the WIC location for each
shift - that is what agents actually ask their team lead about. Propose how, do not build it yet.
