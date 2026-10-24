<div align="center">

# 🔑 NKey

### License & tenant onboarding, built on Power Platform

**The front door to [NFlow](../DASHBOARD)** — issue license keys, approve organization requests and
provision tenants, driven by Dataverse plug-ins and an approval flow instead of hand-written CRUD.

[![Dataverse](https://img.shields.io/badge/Dataverse-Model--driven%20app-742774?logo=microsoftpowerapps&logoColor=white)](https://learn.microsoft.com/power-apps/maker/data-platform/)
[![.NET](https://img.shields.io/badge/.NET%20Framework-4.6.2-512BD4?logo=dotnet&logoColor=white)](https://learn.microsoft.com/power-apps/developer/data-platform/plug-ins)
[![Power Automate](https://img.shields.io/badge/Power%20Automate-Approval%20flow-0066FF?logo=powerautomate&logoColor=white)](https://learn.microsoft.com/power-automate/)
[![PCF](https://img.shields.io/badge/PCF-TypeScript-3178C6?logo=typescript&logoColor=white)](https://learn.microsoft.com/power-apps/developer/component-framework/overview)
[![ALM](https://img.shields.io/badge/ALM-pac%20CLI%20%C2%B7%20GitHub%20Actions-2ea44f)](https://learn.microsoft.com/power-platform/alm/)
[![License](https://img.shields.io/badge/license-Private-red)](#-license)

</div>

> [!NOTE]
> **NKey is the onboarding service NFlow cannot host itself.** NFlow is multi-tenant — an Organization
> is the top-level tenant — but creating the *first* organization is a chicken-and-egg problem: without a
> user you cannot sign in to create one.

---

## 📑 Table of Contents

- [Why Power Platform](#-why-power-platform)
- [How it works](#-how-it-works)
- [Data model](#-data-model)
- [Plug-ins](#-plug-ins)
- [Tech stack](#-tech-stack)
- [Quick start](#-quick-start)
- [Integration with NFlow](#-integration-with-nflow)
- [Project structure](#-project-structure)
- [Security](#-security)

---

## ✨ Why Power Platform

NFlow and NHub are self-hosted .NET — so why is this one built on Dataverse?

Because licensing is **exactly** the workload a model-driven app is good at: low volume, form-heavy,
approval-driven, few users, and more business rules than screens. Writing it as a third .NET service
would repeat work already proven twice; building it here delivers the same product while exercising a
different stack.

- 🧾 **Plans as data, not code** — change capacity or duration by editing a row, not by shipping a release.
- 🔒 **Invariants in plug-ins** — a license binds to at most one organization, enforced server-side in the
  Dataverse pipeline, the same way an aggregate root enforces rules in NFlow.
- 📜 **Immutable audit** — every status change is appended by an async plug-in, with the before/after
  labels frozen as text so history stays readable even if the option set changes.
- 🧮 **Dates derived, never typed** — expiry and payment dates are computed from the plan, so a key can
  never contradict the package it was sold under.
- 🔗 **One source of truth** — Dataverse owns the license; NFlow keeps a read-only snapshot.

---

## 🔄 How it works

```
   Sales                     Ops                        NKey (Dataverse)                    NFlow
     │                        │                                │                              │
     │ create Customer        │                                │                              │
     │ create License Key ───────────────────────────────────▶ │ GenerateLicenseKeyPlugin     │
     │   (pick a Plan)        │                                │   key = base64(NEX-STD-…)     │
     │                        │                                │   expire = today + duration   │
     │                        │                                │   due    = expire − grace     │
     │                        │                                │   status = Issued             │
     │                        │                                │                              │
     │ create Org Request     │                                │                              │
     │   name · alias · admin │                                │                              │
     │                        │ approve ─────────────────────▶ │ ValidateLicenseBindingPlugin │
     │                        │                                │   key is Issued?              │
     │                        │                                │   not expired?                │
     │                        │                                │   not already bound?          │
     │                        │                                │   alias free?                 │
     │                        │                                │        │ pass                 │
     │                        │                                │        ▼                      │
     │                        │                       Power Automate ──────── POST ──────────▶ │ create Organization
     │                        │                       (custom connector)   /internal/v1/       │ + first admin user
     │                        │                                │           orgs/provision      │
     │                        │                                │ ◀──────── orgId · loginUrl ───┤
     │                        │                                │                              │
     │                        │                                │ WriteActivationAuditPlugin   │
     │                        │                                │   Issued → Active, actor, ts │
     │                        │                                │                              │
     │                        │                       email ───┴──────────────────────────────▶ customer signs in
```

**Why three different pipeline stages:** validation runs in **PreValidation** (outside the transaction, so
a rejection costs no rollback), field generation in **PreOperation** (mutating `Target` before the write
needs no second update), and the audit row in **PostOperation / async** (it must only exist if the change
actually committed, and writing history should never block the user).

---

## 🗂 Data model

Five tables, 45 custom columns, four relationships — all `Restrict` on delete.

```
nx_customer ──1:N──► nx_licensekey ──1:N──► nx_orgrequest
                          │
                          └──1:N──► nx_activation

nx_licenseplan ──1:N──► nx_licensekey
```

| Table | Responsibility |
|---|---|
| **`nx_licenseplan`** | Sellable packages — repo capacity, duration in months, grace period |
| **`nx_customer`** | The company buying the license (billing contact, tax code) |
| **`nx_licensekey`** | **Aggregate root** — opaque base64 key, dates, status, bound organization |
| **`nx_orgrequest`** | A request to create an organization, plus the provisioning result |
| **`nx_activation`** | Append-only audit trail: issue · activate · renew · revoke · expire |

### License lifecycle

```
Draft ──► Issued ──► Active ──► Expired
             │          │
             └──────────┴──► Revoked
```

`Status` replaced a boolean `active` flag from the original design: `false` could not tell *"not sold
yet"* apart from *"revoked"*.

<sub>Full column-level spec: [`docs/DATA-MODEL.md`](docs/DATA-MODEL.md) — the single source of truth, kept
in sync with the exported solution.</sub>

---

## 🧩 Plug-ins

C# plug-ins running inside the Dataverse sandbox. Registration details in
[`docs/P2-PLUGINS.md`](docs/P2-PLUGINS.md).

| Plug-in | Message · Table | Stage | Mode | Images |
|---|---|---|---|---|
| **`GenerateLicenseKeyPlugin`** | `Create` · `nx_licensekey` | PreOperation | Sync | — |
| **`ValidateLicenseBindingPlugin`** | `Update` · `nx_orgrequest` | PreValidation | Sync | `PreImage` |
| **`WriteActivationAuditPlugin`** | `Update` · `nx_licensekey` | PostOperation | **Async** | `PreImage` + `PostImage` |

**Binding invariants** — rejected with a message the user actually sees:

| Rule | Enforced because |
|---|---|
| Key must be `Issued` | A draft, revoked or already-active key cannot be provisioned |
| Not past `Expire Date` | Compared on `UtcNow.Date` — the column is Date Only |
| Not already bound | One key ↔ one organization, mirroring NFlow's unique index |
| Org alias still free | Aliases are URL prefixes and unique across all of NFlow |

> [!TIP]
> The last two rules cannot be an alternate key: `nx_boundorgalias` is empty while a key is Draft or
> Issued, and Dataverse alternate keys reject multiple empty values. A `QueryExpression` in the plug-in
> covers what the schema cannot.

---

## 🛠 Tech stack

| Layer | Technology |
|---|---|
| 🗄 **Data & app** | Dataverse · model-driven app · global option sets · alternate key |
| ⚙️ **Business logic** | C# plug-ins — **.NET Framework 4.6.2** (sandbox requirement), `Microsoft.CrmSdk.CoreAssemblies` |
| 🔁 **Workflow** | Power Automate — approval, provisioning call, retry and failure handling |
| 🎨 **UI components** | PCF (TypeScript) — key field, capacity gauge, expiry timeline |
| 🔗 **Integration** | Custom connector → NFlow `/internal/v1/*`, service-token auth |
| 📦 **ALM** | `pac` CLI · solution unpack in git · GitHub Actions |

> [!NOTE]
> `net462` is not a choice. Plug-ins execute in **Microsoft's sandbox process**, not yours — you upload a
> DLL and their host runs it. `<LangVersion>latest</LangVersion>` still works, so the modern C# syntax is
> available; only the newer BCL is not.

---

## ⚡ Quick start

**Prerequisites** — a Power Platform environment with Dataverse, and the CLI:

```powershell
dotnet tool install --global Microsoft.PowerApps.CLI.Tool
pac help     # version is printed in the banner; `pac --version` is not a command
```

```powershell
# 1. Connect to your environment
pac auth create --name NexusDev --environment <Environment URL>
pac org who

# 2. Import the solution
.\scripts\import-solution.ps1 -Profile NexusDev -Unmanaged

# 3. Build and register the plug-ins
cd src\plugins\Nexus.Licensing.Plugins
dotnet build
pac tool prt     # Plugin Registration Tool — register the assembly, steps and images
```

Environment identifiers live in `docs/ENVIRONMENT.local.md` (gitignored) — copy
[`docs/ENVIRONMENT.local.example.md`](docs/ENVIRONMENT.local.example.md) and fill it in.
Setup walkthrough: [`docs/P0-SETUP.md`](docs/P0-SETUP.md).

---

## 🔗 Integration with NFlow

NKey calls NFlow; never the other way around in the provisioning path.

| Direction | Mechanism |
|---|---|
| NKey → NFlow | REST to `/internal/v1/*` with an `X-Internal-Token` header |
| NFlow ← snapshot | `Organization.LicenseKey` · `LicenseDueDate` · `LicenseExpireDate` · `LicenseRepoCapacity` |

Two endpoints are required on the NFlow side:

```http
POST /internal/v1/orgs/provision      → { orgId, orgAlias, loginUrl }
GET  /internal/v1/orgs/{alias}/usage  → { repoCount, repoCapacity, licenseExpireDate }
```

> [!IMPORTANT]
> **Dataverse owns the license.** NFlow holds a read-only copy taken at activation — editing a license
> there would silently diverge from the source of truth. Capacity is enforced in NFlow at
> `CreateRepositoryCommandHandler`, using that snapshot.

---

## 📂 Project structure

```text
solution/
└── NexusLicensing/                      # unpacked solution — source of truth in git
    ├── Entities/                        # nx_licenseplan · nx_customer · nx_licensekey
    │                                    # nx_orgrequest · nx_activation
    ├── OptionSets/                      # global choices (values start at 0, not 100000000)
    ├── AppModuleSiteMaps/               # model-driven app navigation
    └── Other/                           # Solution.xml, Customizations.xml, Relationships

src/
├── plugins/
│   └── Nexus.Licensing.Plugins/         # net462, strong-named
│       ├── GenerateLicenseKeyPlugin.cs
│       ├── ValidateLicenseBindingPlugin.cs
│       ├── WriteActivationAuditPlugin.cs
│       ├── LicensingConstants.cs        # logical names + option set values in one place
│       └── PluginBase.cs                # IServiceProvider wrapper, tracing, error handling
└── pcf/                                 # PCF controls (planned)

scripts/
├── export-solution.ps1                  # export + unpack into git
├── import-solution.ps1                  # pack + import into an environment
├── verify-schema.ps1                    # tables · date behavior · cascade · alternate key
├── verify-licenseplan.ps1               # Min/Max/MaxLength constraints
└── fetch/*.xml                          # reusable FetchXML queries

docs/
├── DATA-MODEL.md                        # column-level spec — source of truth
├── P0-SETUP.md                          # environment setup
├── P1-DATA-MODEL.md                     # how the tables were built
└── P2-PLUGINS.md                        # plug-in registration + test cases
```

---

Things that cost real time here, kept so they cost it only once.

| Trap | What happens | Way out |
|---|---|---|
| **Date behavior is immutable** | Picked once at column creation and never changeable | Set it right the first time — see [`docs/DATA-MODEL.md`](docs/DATA-MODEL.md) §8 |
| **`Time-Zone Independent` is one-way** | Delete and recreate under the same name still returns it | Drop the column entirely; a lost `Issued On` is why it is gone |
| **Two look-alike dropdowns** | "Behavior" is Simple/Calculated/Rollup — the date behavior is under **Time zone adjustment** | Read the field label, not the position |
| **Canvas table editor** | Silently skips Min/Max, MaxLength *and* date behavior | Always build tables from inside the solution editor |
| **Global option sets start at 0** | Not `100000000` as local ones do | Values are centralised in `LicensingConstants.cs` |
| **Plugin Registration Tool and MFA** | Its embedded browser has no WebAuthn, so passkeys never sign in | Add an authenticator one-time code — [`docs/P0-SETUP.md`](docs/P0-SETUP.md) |
| **Forms start nearly empty** | A new table's form shows only the primary column and Owner | Add every column manually, then publish |

---

## 🔐 Security

| Item | Handling |
|---|---|
| Tenant name · admin email · environment URL · GUIDs | `docs/ENVIRONMENT.local.md` — **gitignored** |
| Plug-in signing key (`.snk`) | **Committed**, per Microsoft guidance |
| Service token to NFlow | Environment variable in the connector, never in source |

> [!NOTE]
> A strong name **identifies** an assembly, it does not secure it. Regenerating the key makes Dataverse
> treat the build as a different assembly, forcing every step and image to be registered again — so the
> `.snk` belongs in the repository.

---

<div align="center">

**Private repository — all rights reserved.**

<sub>Part of the Nexus suite: [NFlow](../DASHBOARD) · [NHub](../HUB) · [NKey](../LICENSE/)</sub>

</div>
