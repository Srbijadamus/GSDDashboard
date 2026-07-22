SELECT id, employee_id, first_name, last_name, team_leader, location
FROM agents
WHERE active = 1
ORDER BY last_name, first_name;
