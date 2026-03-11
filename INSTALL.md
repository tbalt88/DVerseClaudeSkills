# DVerseClaudeSkills

Sample Microsoft Dataverse backend components generated using **Claude Code** with the `d365-architect` skill. Covers PreValidation plug-ins, Dataverse workflow extensions, and Custom API patterns — each with an isolated VS project, unit tests, and documentation.

---

## Prerequisites

| Requirement | Notes |
|---|---|
| [Claude Code](https://claude.ai/claude-code) | CLI installed and authenticated |
| .NET Framework 4.6.2 SDK | Required to build plug-in assemblies |
| Visual Studio 2022 (or VS Code + C# extension) | Open individual `.csproj` files per component |
| NuGet access | Packages restore on build automatically |
| Strong-name key | Generate once per project: `sn -k key.snk` |

---

## Installing the Skill in Claude Code

The `d365-architect` skill provides domain-grounded Dataverse architecture and implementation guidance inside Claude Code. The skill definition is included in this repo under `d365-architect/`.

### 1. Clone this repo

```bash
git clone https://github.com/tbalt88/DVerseClaudeSkills.git
```

### 2. Copy the skill into Claude Code's skills folder

**Windows (PowerShell):**
```powershell
Copy-Item -Recurse "DVerseClaudeSkills\d365-architect" "$env:USERPROFILE\.claude\skills\d365-architect"
```

**Mac / Linux:**
```bash
cp -r DVerseClaudeSkills/d365-architect ~/.claude/skills/d365-architect
```

Claude Code automatically discovers skills placed in `~/.claude/skills/`.

### 3. Open Claude Code

```bash
claude
```

### 4. Invoke the skill

In the Claude Code prompt, type:

```
/d365-architect
```

### 5. Set your project context

On first use, tell Claude Code your project conventions once:

```
prefix = <your_prefix>, solution = <your_solution_name>
```

Example used in this repo:

```
prefix = dexx, solution = CoreSolution
```

Claude Code saves these to persistent memory — no need to repeat them in future sessions.

---

## Project Structure

Each Dataverse backend component follows this structure, generated automatically by the skill:

```
CoreSolution/
  Plugins/
    <entity>/
      <PluginName>.cs           — plug-in or workflow activity logic
      <PluginName>.csproj       — isolated VS project (net462, strong-named)
      <PluginName>.md           — component documentation
      Tests/
        <PluginName>.Tests.csproj   — isolated test project
        <PluginName>Tests.cs        — xUnit + Moq unit tests
```

---

## Components in This Repo

### `dexx_order` — BlockOrderCreateOnCreditHold
- **Type**: PreValidation plug-in
- **Trigger**: `dexx_order` Create
- **Logic**: Blocks order creation if the related Account has `creditonhold = true`

### `account` — CreateOpportunityOnStatusChange
- **Type**: Dataverse workflow extension (`CodeActivity`)
- **Trigger**: Account `statuscode` updated to `100000`
- **Logic**: Creates an Opportunity + 15-day follow-up Task, resets Account status, returns `OutSuccess` and `OutOpportunityId` to the workflow designer

---

## Building

Open any `.csproj` directly in Visual Studio, or build from the command line:

```bash
dotnet build CoreSolution/Plugins/dexx_order/BlockOrderCreateOnCreditHold.csproj
dotnet test  CoreSolution/Plugins/dexx_order/Tests/BlockOrderCreateOnCreditHold.Tests.csproj
```

> Generate the strong-name key before the first build:
> ```bash
> sn -k CoreSolution/Plugins/dexx_order/key.snk
> sn -k CoreSolution/Plugins/account/key.snk
> ```

---

## Registering Plug-ins

Use the [Plug-in Registration Tool](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/download-tools-nuget) or `pac plugin push` to deploy assemblies to your Dataverse environment.

| Component | Entity | Message | Stage | Mode |
|---|---|---|---|---|
| BlockOrderCreateOnCreditHold | `dexx_order` | Create | PreValidation (10) | Sync |
| CreateOpportunityOnStatusChange | `account` | Update | — | Async (workflow) |

---

## Skill Conventions (Persisted in Memory)

| Convention | Value |
|---|---|
| One `.csproj` per plug-in | Inside the entity subfolder |
| Test project | `Tests/` subfolder, one per plug-in |
| Documentation | `<PluginName>.md` alongside the `.cs` file |
| Test framework | xUnit 2.6.6 + Moq 4.20.70 |
| Target framework | `net462` |
| Exception handling | IPEE re-thrown; all others traced + wrapped |

---

## License

MIT
