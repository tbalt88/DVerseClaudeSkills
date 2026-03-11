# CE ALM Reference
> Source: MS Docs — "Use the SolutionPackager tool to compress and extract a
> solution file", "Solution Tools for Team Development", "Use Source Control
> with Solution Files", "Introduction to Solutions", pac CLI reference

---

## SolutionPackager — Official Tool

SolutionPackager reversibly decomposes a `.zip` solution file into individual
XML files for source control. Distributed as part of the
`Microsoft.CrmSdk.CoreTools` NuGet package. `pac solution` wraps it.

### Managed vs Unmanaged (Official Definition)
- **Unmanaged:** Open solution; components can be added/removed/modified.
  Use in Dev environments only.
- **Managed:** Components cannot be added/removed after import; locked for
  customization (unless `isCustomizable=true`). Deploy to Test and Prod only.
- **Cannot convert between types locally** — import unmanaged to Dataverse,
  export as managed.

---

## pac CLI — Primary ALM Interface

```bash
# Auth — service principal (preferred for CI/CD)
pac auth create \
  --url https://yourorg.crm.dynamics.com \
  --applicationId $PP_APP_ID \
  --clientSecret $PP_CLIENT_SECRET \
  --tenant $PP_TENANT_ID \
  --name dev-auth

pac auth list                     # list profiles
pac auth select --index 1         # switch active profile

# Environment management
pac admin create-environment \
  --name "MySolution-Dev" \
  --type Sandbox \
  --region unitedstates \
  --currency USD --language 1033
pac env list                      # list all environments

# Solution operations
pac solution list                 # list solutions in active env

pac solution export \
  --name MySolution \
  --path ./export/MySolution.zip \
  --managed false                 # export unmanaged from Dev

pac solution unpack \
  --zipfile ./export/MySolution.zip \
  --folder ./src/solutions/MySolution \
  --packagetype Unmanaged

pac solution pack \
  --zipfile ./dist/MySolution_managed.zip \
  --folder ./src/solutions/MySolution \
  --packagetype Managed

pac solution import \
  --path ./dist/MySolution_managed.zip \
  --activate-plugins true \
  --force-overwrite true

# Plugin tooling
pac plugin init --skip-signing    # scaffold PluginBase project
pac tool prt                      # launch Plug-in Registration Tool
```

---

## Repo Structure (Source Control Pattern)

```
ce-solutions/
├── .github/
│   └── workflows/
│       ├── export-unpack.yml      ← manual: export from Dev
│       └── pack-import.yml        ← auto: pack + deploy on push to main
├── src/
│   ├── solutions/
│   │   └── MySolution/            ← unpacked solution XML
│   │       ├── solution.xml
│   │       ├── Entities/
│   │       ├── WebResources/
│   │       └── PluginAssemblies/
│   └── plugins/
│       └── MyPlugin/              ← plugin C# project
│           ├── MyPlugin.csproj
│           ├── OnCreateOrder.cs
│           └── MyPlugin.snk
├── .gitignore
└── README.md
```

**.gitignore (mandatory entries):**
```
bin/
obj/
*.dll
*.zip
export/
dist/
*.user
.vs/
```

---

## Export Pipeline (workflow_dispatch — manual trigger)

```yaml
# .github/workflows/export-unpack.yml
name: Export Solution from Dev
"on":
  workflow_dispatch:
    inputs:
      solution_name:
        description: 'Solution logical name'
        required: true
        default: 'MySolution'

jobs:
  export:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Install pac CLI
        run: |
          dotnet tool install --global Microsoft.PowerApps.CLI.Tool
          echo "$HOME/.dotnet/tools" >> $GITHUB_PATH

      - name: Authenticate to Dev
        run: |
          pac auth create \
            --url ${{ vars.DEV_ENV_URL }} \
            --applicationId ${{ secrets.PP_APP_ID }} \
            --clientSecret ${{ secrets.PP_CLIENT_SECRET }} \
            --tenant ${{ secrets.PP_TENANT_ID }}

      - name: Export unmanaged solution
        run: |
          pac solution export \
            --name ${{ inputs.solution_name }} \
            --path ./export/${{ inputs.solution_name }}.zip \
            --managed false

      - name: Unpack solution
        run: |
          pac solution unpack \
            --zipfile ./export/${{ inputs.solution_name }}.zip \
            --folder ./src/solutions/${{ inputs.solution_name }} \
            --packagetype Unmanaged

      - name: Commit and push
        run: |
          git config user.email "ci@your-org.com"
          git config user.name "GitHub Actions"
          git add src/solutions/
          git diff --staged --quiet || \
            git commit -m "chore: export ${{ inputs.solution_name }} [skip ci]"
          git push
```

---

## Pack + Import Pipeline (push to main — auto deploy)

```yaml
# .github/workflows/pack-import.yml
name: Pack and Deploy Solution
"on":
  push:
    branches: [main]
    paths: ['src/**']

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Install pac CLI
        run: |
          dotnet tool install --global Microsoft.PowerApps.CLI.Tool
          echo "$HOME/.dotnet/tools" >> $GITHUB_PATH

      - name: Build plugin assembly
        run: dotnet build src/plugins/MyPlugin --configuration Release

      - name: Pack managed solution
        run: |
          pac solution pack \
            --zipfile ./dist/MySolution_managed.zip \
            --folder ./src/solutions/MySolution \
            --packagetype Managed

      - name: Upload artifact
        uses: actions/upload-artifact@v4
        with:
          name: solution-artifact
          path: dist/MySolution_managed.zip

  deploy-test:
    needs: build
    runs-on: ubuntu-latest
    steps:
      - name: Download artifact
        uses: actions/download-artifact@v4
        with: { name: solution-artifact, path: dist/ }

      - name: Install pac CLI
        run: |
          dotnet tool install --global Microsoft.PowerApps.CLI.Tool
          echo "$HOME/.dotnet/tools" >> $GITHUB_PATH

      - name: Deploy to Test
        run: |
          pac auth create \
            --url ${{ vars.TEST_ENV_URL }} \
            --applicationId ${{ secrets.PP_APP_ID }} \
            --clientSecret ${{ secrets.PP_CLIENT_SECRET }} \
            --tenant ${{ secrets.PP_TENANT_ID }}
          pac solution import \
            --path ./dist/MySolution_managed.zip \
            --activate-plugins true --force-overwrite true

  deploy-prod:
    needs: deploy-test
    runs-on: ubuntu-latest
    environment: production        # ← GitHub Environment protection rule = manual approval
    steps:
      - name: Download artifact
        uses: actions/download-artifact@v4
        with: { name: solution-artifact, path: dist/ }

      - name: Install pac CLI
        run: |
          dotnet tool install --global Microsoft.PowerApps.CLI.Tool
          echo "$HOME/.dotnet/tools" >> $GITHUB_PATH

      - name: Deploy to Prod
        run: |
          pac auth create \
            --url ${{ vars.PROD_ENV_URL }} \
            --applicationId ${{ secrets.PP_APP_ID }} \
            --clientSecret ${{ secrets.PP_CLIENT_SECRET }} \
            --tenant ${{ secrets.PP_TENANT_ID }}
          pac solution import \
            --path ./dist/MySolution_managed.zip \
            --activate-plugins true --force-overwrite true
```

---

## Required GitHub Secrets and Variables

```bash
# Secrets (sensitive — encrypted)
gh secret set PP_APP_ID          # AAD app registration client ID
gh secret set PP_CLIENT_SECRET   # AAD client secret
gh secret set PP_TENANT_ID       # AAD tenant ID

# Variables (non-sensitive — visible in logs)
gh variable set DEV_ENV_URL   --body "https://yourorg-dev.crm.dynamics.com"
gh variable set TEST_ENV_URL  --body "https://yourorg-test.crm.dynamics.com"
gh variable set PROD_ENV_URL  --body "https://yourorg.crm.dynamics.com"
```

---

## SolutionPackager Map File (for plugin DLL redirection)

```xml
<!-- solution-map.xml — redirect DLL from build output, not solution folder -->
<?xml version="1.0" encoding="utf-8"?>
<Mapping>
  <FileToFile
    map="PluginAssemblies\**\MyPlugin.dll"
    to="src\plugins\MyPlugin\bin\Release\netstandard2.0\MyPlugin.dll" />
  <FileToPath
    map="WebResources\**\*.*"
    to="src\webresources\**" />
</Mapping>
```

> Use `/map` with SolutionPackager / `pac solution pack --map` to avoid
> committing compiled DLLs to source control. (SolutionPackager docs)
