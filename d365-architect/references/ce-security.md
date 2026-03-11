# CE Security Model Reference
> Source: MS Docs — "Security model of Customer Engagement", "How Role-Based
> Security Can Be Used to Control Access to Entities", "How Instance-Based
> Security Can Be Used to Control Access to Records", "How Field Security Can Be
> Used to Control Access to Field Values", "Hierarchical security" (CE v9.1)

---

## Three-Layer Security Model (Official)

> "Combine role-based security, record-level security, and field-level security
> to define the overall security rights that users have." (Security model docs)

| Layer | Scope | Mechanism |
|---|---|---|
| **Role-Based** | Entity-level CRUD + privileges | Security Roles → assigned to Users or Teams |
| **Record-Based** | Row-level ownership + sharing | Record Owner; Share; Access Teams |
| **Field-Level (FLS)** | Column-level read/write | Field Security Profiles → assigned to Users or Teams |

---

## Role-Based Security

Security roles aggregate privileges for an entity across CRUD + Append +
Append To + Share + Assign — each with an **access level scope**:

| Access Level | Scope |
|---|---|
| User | Records owned by the user |
| Business Unit | Records owned by users in the same BU |
| Parent: Child BUs | Records owned by users in the BU and all child BUs |
| Organization | All records in the org |

### Design pattern: assign roles to Teams, not individual users
```
AAD Group → Dataverse Team (Owner Team or Access Team) → Security Role(s)
```
- Owner Teams: members inherit the team's security roles
- Access Teams: per-record sharing; no inherited roles
- Sync AAD groups to Owner Teams for automated provisioning at scale

### BU Hierarchy Design

Record ownership flows up the BU tree. A user with Parent:Child BU access
in Region-Americas sees all records owned by users under Americas (including
sub-BUs US-West, US-East, LATAM).

```
Root BU (Org)
├── Americas
│   ├── US-West
│   └── US-East
└── EMEA
    ├── UK
    └── DACH
```

| Pattern | Use When |
|---|---|
| Flat (all users in Root BU) | Small org, no data isolation needed |
| Regional | Geographic data isolation (field reps see own territory) |
| Functional | Separate BUs per department (Sales, Service, Finance) |
| Matrix | Both regional AND functional isolation (complex) |

> **⚠️ Post-go-live warning:** BU structure is very hard to change after
> records exist. Every record is owned by a user in a BU — moving records
> requires manual bulk reassignment. Design and sign-off BU hierarchy in
> week 1. Moving an HCP from US-West to US-East requires reassigning every
> record they own.

---

## Record-Based Security

Three mechanisms for row-level access beyond role-based:

**1. Record Ownership**
- Every record has an Owner (user or team)
- Owner always has full access regardless of role access level

**2. Sharing (explicit)**
```csharp
// Share a record with a user (granting specific rights)
var grantRequest = new GrantAccessRequest
{
    Target = new EntityReference("dexx_order", orderId),
    PrincipalAccess = new PrincipalAccess
    {
        Principal = new EntityReference("systemuser", userId),
        AccessMask = AccessRights.ReadAccess | AccessRights.WriteAccess
    }
};
orgService.Execute(grantRequest);
```

**3. Access Teams (per-record, dynamic sharing)**
- Define Access Team Template on entity
- Add users to per-record access team at runtime
- No security role inheritance — just access to that specific record

---

## Field-Level Security (FLS)

Restricts read/write on specific columns regardless of role-based access.
A user can have full Org-level access to Account but be locked out of
`dexx_creditlimit` field.

**Setup steps:**
1. Enable field-level security on the column (entity customization)
2. Create a Field Security Profile (FSP)
3. Set Read / Create / Update permissions per field in FSP
4. Add Users or Teams to the FSP

```csharp
// Check if field is secured — use FieldPermission table in code
// FLS is enforced by the platform; plugins cannot bypass it
// To read secured fields in a plugin: the plugin runs as UserId,
// so ensure the service account has appropriate FSP assignment
```

**Anti-patterns:**
- Using FLS on fields updated by plugins with a service account that lacks FSP
- Applying FLS to fields used in workflow conditions (workflows may not evaluate)
- FLS on fields that appear in views users need to see (invisible in grid)

---

## Hierarchical Security

Optional add-on to role-based security for manager chains.

- **Position hierarchy:** custom hierarchy based on Position records
- **Manager hierarchy:** based on SystemUser.manager_id chain

With hierarchy security enabled, a manager at depth N can access records
owned by direct reports (depth controlled by `Hierarchy Depth` setting, max 3).

> Use hierarchical security only when org has deep management reporting needs.
> Most healthcare/pharma D365 implementations use regional BU + teams instead.

---

## Service Principal / Application User Security

For pipelines, integrations, and Azure Functions authenticating to Dataverse:

```bash
# 1. Register AAD app (az CLI)
az ad app create --display-name "D365-Pipeline-SP"
az ad sp create --id <appId>
az ad app credential reset --id <appId> --append

# 2. In Dataverse: Settings > Security > Application Users
#    Create Application User, set the Client ID (App ID)
#    Assign a Security Role (least privilege; create custom Deployment role)

# 3. Verify app user exists and is active
pac admin list-app-users --environment <envId>
```

**Principle of least privilege for app users:**
- System Administrator only during initial deployment setup
- Create a custom "Deployment" security role for CI/CD pipelines with:
  - Solution import/export privileges
  - Plugin assembly read/write
  - Web resource read/write
- Service accounts for integrations: only the entity-level CRUD they need

---

## Anti-Patterns

| Anti-pattern | Problem | Fix |
|---|---|---|
| Assign Security Roles directly to users | Unmanageable at scale; no group dynamics | Assign to Teams; sync AAD groups |
| System Administrator for service accounts | Maximum blast radius on breach | Custom role with minimum needed privileges |
| Single monolithic Security Role | Hard to audit; breaks separation of concerns | Persona-based roles (Field Rep, Manager, Admin) |
| Modifying OOB Security Roles | Overwritten on solution update | Copy OOB role, modify copy |
| FLS on plugin-read fields without FSP on app user | Plugin fails silently or reads null | Add app user to FSP with read permission |
| Designing BU hierarchy post-go-live | Requires mass record reassignment | Design and freeze BU hierarchy before go-live |
