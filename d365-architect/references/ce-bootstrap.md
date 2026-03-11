# CE Environment Bootstrap Reference
> Source: MS Docs — "Get started with the SDK", "Developer tools",
> "Choose your development style", pac CLI reference, AAD app registration
> (CE on-premises v9.1 + Power Platform Admin docs)

---

## Day-1 Runbook: New CE Solution from Zero

### Phase 1 — AAD App Registration (service principal)

```bash
# 1. Create AAD app for pipeline + integration auth
az login
az ad app create --display-name "D365-MySolution-SP"

# 2. Create service principal
APP_ID=$(az ad app list --display-name "D365-MySolution-SP" \
  --query "[0].appId" -o tsv)
az ad sp create --id $APP_ID

# 3. Create client secret (save output — shown once)
az ad app credential reset --id $APP_ID --append
# Save: appId, password (secret), tenant

# 4. In Dataverse: Settings → Security → Application Users
#    → New Application User → paste App ID
#    → Assign System Administrator role ONLY for initial setup
#    → After bootstrap: swap to minimum-privilege custom role
```

---

### Phase 2 — Publisher and Solution Setup

```bash
# Authenticate pac CLI to Dev environment
pac auth create \
  --url https://yourorg-dev.crm.dynamics.com \
  --applicationId $APP_ID \
  --clientSecret $CLIENT_SECRET \
  --tenant $TENANT_ID \
  --name dev

# Create publisher (do this ONCE — prefix cannot change after records exist)
# In UI: Settings → Solutions → Publishers → New
# Or via pac (confirm publisher exists before creating solution)
pac org who    # verify connected org

# Create solution (in UI or import via XML)
# Recommended: create in UI first, then export/unpack for source control
```

**Publisher settings (critical — set before any customization):**
| Setting | Example | Rule |
|---|---|---|
| Display Name | Contoso | Human-readable |
| Name | contoso | Unique, no spaces |
| Prefix | dexx | 2–8 lowercase letters; used for ALL custom schema names |
| Option value prefix | 12345 | Auto-assigned; leave default |

> **Once the prefix is in use on live records, it cannot be changed.**
> All custom tables, columns, relationships, and web resources will carry
> this prefix forever. Choose carefully.

---

### Phase 3 — Plugin Project Scaffold

```bash
# Option A: Official pac plugin init (recommended — generates PluginBase)
mkdir -p src/plugins && cd src/plugins
pac plugin init --skip-signing
# → Creates: PluginBase.cs, Plugin1.cs, MyPlugin.csproj

# Rename Plugin1.cs to your first plugin class
mv Plugin1.cs OnCreateOrder.cs

# Build to verify
dotnet build --configuration Release

# Option B: Manual classlib (full control)
dotnet new classlib -n MyPlugin --framework netstandard2.0
cd MyPlugin
dotnet add package Microsoft.CrmSdk.CoreAssemblies --version 9.0.2.62
dotnet add package Microsoft.CrmSdk.Workflow --version 9.0.2.62
dotnet build --configuration Release

# Strong-name sign (required for Sandbox isolation mode)
sn -k MyPlugin.snk
# Add to .csproj:
# <AssemblyOriginatorKeyFile>MyPlugin.snk</AssemblyOriginatorKeyFile>
# <SignAssembly>true</SignAssembly>
```

---

### Phase 4 — First Solution Export to Source Control

```bash
# Init git repo
git init
git remote add origin https://github.com/your-org/ce-solutions.git

# Create directory structure
mkdir -p .github/workflows src/solutions src/plugins src/webresources

# .gitignore
cat > .gitignore << 'EOF'
bin/
obj/
*.dll
*.zip
export/
dist/
*.user
.vs/
*.snk
EOF

# Export unmanaged solution from Dev
mkdir -p export
pac solution export \
  --name MySolution \
  --path ./export/MySolution.zip \
  --managed false

# Unpack into source control
pac solution unpack \
  --zipfile ./export/MySolution.zip \
  --folder ./src/solutions/MySolution \
  --packagetype Unmanaged

# Commit baseline
git add .
git commit -m "feat: initial solution scaffold"
git push -u origin main
```

---

### Phase 5 — GitHub Repository Setup

```bash
# Set GitHub secrets (pipeline auth)
gh secret set PP_APP_ID --body "$APP_ID"
gh secret set PP_CLIENT_SECRET --body "$CLIENT_SECRET"
gh secret set PP_TENANT_ID --body "$TENANT_ID"

# Set environment URLs as variables (visible in logs — not sensitive)
gh variable set DEV_ENV_URL  --body "https://yourorg-dev.crm.dynamics.com"
gh variable set TEST_ENV_URL --body "https://yourorg-test.crm.dynamics.com"
gh variable set PROD_ENV_URL --body "https://yourorg.crm.dynamics.com"

# Create GitHub Environment for production gate (manual approval)
# GitHub UI: Settings → Environments → New environment → "production"
# → Add required reviewers → Save
```

---

### Phase 6 — Plug-in Registration Tool Setup

```bash
# Launch PRT via pac (downloads and runs interactively)
pac tool prt

# In PRT:
# 1. Create New Connection → Microsoft 365 / On-Premises
# 2. Register New Assembly → browse to bin/Release/netstandard2.0/MyPlugin.dll
#    → Isolation Mode: Sandbox (online) / None (on-premises full trust)
#    → Location: Database (online) / Disk (on-premises)
# 3. Register New Step for each plugin class:
#    → Message: Create / Update / Delete / etc.
#    → Primary Entity: dexx_mytable (logical name)
#    → Stage: PreValidation / PreOperation / PostOperation
#    → Execution Mode: Synchronous / Asynchronous
#    → Filtering Attributes: (Update steps only — set specific columns)
# 4. Register Entity Images if needed:
#    → Pre-image: snapshot of record BEFORE update
#    → Post-image: snapshot AFTER (PostOp only)
```

---

### Phase 7 — Dev Tooling Verification Checklist

```bash
# Verify all tools are present and working
pac --version                    # Power Platform CLI
dotnet --version                 # .NET SDK (6.0+ recommended)
az --version                     # Azure CLI (for AAD + token ops)
gh --version                     # GitHub CLI (for secrets/variables)
git --version                    # Git
node --version                   # Node.js (for XrmToolBox scripts)

# Verify pac can reach org
pac org who

# Verify solution list
pac solution list

# Verify plugin list (after first registration)
pac plugin list
```

---

## Tools Reference

| Tool | Install | Use |
|---|---|---|
| pac CLI | `dotnet tool install -g Microsoft.PowerApps.CLI.Tool` | ALM, auth, env management |
| Plug-in Registration Tool | `pac tool prt` | Plugin + step + image registration |
| XrmToolBox | https://www.xrmtoolbox.com | FetchXML builder, metadata browser |
| SolutionPackager | Ships with `Microsoft.CrmSdk.CoreTools` NuGet | Pack/unpack solution ZIP |
| Solution Checker | `pac solution check` | Static analysis, best practice violations |
| Configuration Migration | `pac tool cmt` | Migrate reference/config data |

---

## Common First-Day Mistakes

| Mistake | Impact | Prevention |
|---|---|---|
| Using wrong publisher prefix | All schema names wrong; cannot rename | Set publisher prefix before ANY customization |
| Not signing plugin assembly | Registration fails in Sandbox mode | `sn -k` + add snk to .csproj before first build |
| Using System Admin for pipeline forever | Security risk | Swap to minimum-privilege role after bootstrap |
| Skipping source control setup | No history; can't roll back | Day-1 git init + export pipeline before any dev |
| Deploying unmanaged to Prod | Unlocked solution; anyone can break it | Always import managed to Test/Prod |
| Not setting FilteringAttributes on Update | Plugin fires on every field update | Set in PRT step registration immediately |
