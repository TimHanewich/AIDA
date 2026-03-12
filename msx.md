An **opportunity** has a **team**.

A **team** has **teammemberships** ("bridge" record for N:N)

Each **teammembership** ties back to a **systemuser**.




So to find opportunities someone is on:
1. Get ID of a systemuser (search by name).
2. Find teammemberships for a certain systemuser.
    - `https://microsoftsales.crm.dynamics.com/api/data/v9.2/teammemberships?$filter=systemuserid eq '4a096251-7aaf-ee11-a569-0022482f3ff5'&$select=teamid`
3. For each teammembership, get the "Deal Team", "Opportunity", and "Account" it corresponds to, all in one!
    - `https://microsoftsales.crm.dynamics.com/api/data/v9.2/teams(ea1e1a3a-cc12-f111-8406-002248299577)?$select=teamid&$expand=regardingobjectid_opportunity($select=name;$expand=parentaccountid($select=accountid,name))`
    - That above will:
    - Pull down that team.
    - Expand the opportunity it is tied to (polymorphous)
    - Expand the account info WITHIN that opportunity it is tied to (nested expand)