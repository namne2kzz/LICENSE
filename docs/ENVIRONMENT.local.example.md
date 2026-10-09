# Thông tin môi trường — mẫu

> Copy file này thành `docs/ENVIRONMENT.local.md` rồi điền giá trị thật.
> File `.local.md` đã được gitignore — **không bao giờ commit**.

| Mục | Giá trị |
|---|---|
| Tenant | `<tên>.onmicrosoft.com` |
| Tài khoản admin | `admin@<tên>.onmicrosoft.com` |
| MFA | Microsoft Authenticator (one-time code) |
| Environment | `Nexus-Dev` |
| Environment URL | `https://org########.crm#.dynamics.com` |
| Region | `crm#` |
| Environment ID | `########-####-####-####-############` |
| Org ID | `########-####-####-####-############` |
| Dynamics 365 apps | `No` (irreversible) |
| pac auth profile | `NexusDev` |
| Publisher | `Nexus` / prefix `nx` — id `########-####-####-####-############` |
| Solution | `NexusLicensing` v1.0.0.0 — id `########-####-####-####-############` |

Lấy lại giá trị bất cứ lúc nào:

```powershell
pac org who                                     # Org ID, Environment ID, URL, tài khoản
pac solution list                               # solution + version
pac env fetch --xmlFile .\scripts\fetch\publishers.xml   # publisher id + prefix
```
