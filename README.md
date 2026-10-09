# LICENSE — Nexus Licensing (Power Platform)

> Service onboarding & license cho hệ sinh thái **DASHBOARD** + **HUB**, xây trên **Power Platform / Dataverse**.
> Repo riêng, tách khỏi DASHBOARD — cùng mô hình với HUB.

---

## Vì sao service này tồn tại

DASHBOARD là multi-tenant: `Organization` = tenant cấp cao, 1 Org = 1 công ty mua license. Nhưng việc **tạo Org đầu tiên** không thể nằm trong DASHBOARD vì chicken-and-egg — chưa có user thì không login được để tạo org.

Vì vậy onboarding/license tách ra thành service riêng. Luồng create-org từng được build trong DASHBOARD rồi **gỡ bỏ** (2026-08-23); DASHBOARD hiện chỉ dựa vào seed default org.

**LICENSE lấp khoảng trống đó**, và được xây trên Power Platform vì nghiệp vụ này đúng sweet spot của model-driven app: low-volume, nhiều form + approval workflow + phân quyền, ít người dùng.

---

## Nguyên tắc cứng (non-negotiable)

1. **Dataverse là source of truth của license.** DASHBOARD chỉ giữ **snapshot read-only** trong `Organization.LicenseKey / LicenseDueDate / LicenseExpireDate / LicenseRepoCapacity`. Không sửa license từ UI DASHBOARD.
2. **LICENSE gọi DASHBOARD, không bao giờ ngược lại ở luồng provision.** Qua `/internal/v1/*` với header `X-Internal-Token` (service-token auth có sẵn).
3. **Không đọc DB DASHBOARD trực tiếp.** Chỉ REST — giống ràng buộc của HUB.
4. **Solution commit dạng unpack**, không commit `.zip`. Mọi thay đổi trên cloud phải `pac solution export` + `unpack` về repo trước khi kết thúc buổi làm.
5. **Managed solution cho Test/Prod**, unmanaged chỉ ở Dev.

---

## Cấu trúc repo

```
LICENSE/
├── solution/
│   └── NexusLicensing/          # solution unpack (source of truth trong git)
│       ├── Other/               # Solution.xml, Customizations.xml
│       ├── Entities/            # nx_licenseplan, nx_customer, nx_licensekey, nx_orgrequest, nx_activation
│       ├── Workflows/           # Power Automate flow definitions
│       └── WebResources/
├── src/
│   ├── plugins/                 # Dataverse plug-in C# (.NET Framework 4.6.2)
│   │   └── Nexus.Licensing.Plugins/
│   └── pcf/                     # PCF controls (TypeScript)
│       ├── LicenseKeyField/
│       ├── CapacityGauge/
│       └── ExpiryTimeline/
├── scripts/                     # pac CLI helper scripts (export/unpack/pack/import)
├── docs/                        # tài liệu triển khai chi tiết
│   ├── DATA-MODEL.md            # ĐẶC TẢ table/column — nguồn sự thật
│   ├── P0-SETUP.md              # checklist dựng môi trường
│   └── P1-DATA-MODEL.md         # quy trình thao tác P1
└── .github/workflows/           # ALM pipeline (export → pack → import)
```

---

## Data model (Dataverse)

| Table | Vai trò |
|---|---|
| `nx_licenseplan` | Gói bán sẵn: capacity, thời hạn, grace period |
| `nx_customer` | Khách hàng mua license |
| `nx_licensekey` | Key (alternate key = unique), due/expire date, repo capacity, status, org đã bind |
| `nx_orgrequest` | Yêu cầu tạo org: tên, alias, admin email → kết quả provision |
| `nx_activation` | Audit trail: issue / activate / renew / revoke / expire |

**Đặc tả đầy đủ (nguồn sự thật): [docs/DATA-MODEL.md](docs/DATA-MODEL.md)** — 5 table · 46 column · 4 relationship, max length khớp ràng buộc thật của DASHBOARD (`Organization.cs` + `OrganizationConfiguration.cs`).

---

## Luồng nghiệp vụ

```
Sales tạo nx_customer + nx_orgrequest
   └─ Plug-in PreOperation: sinh license key base64 opaque
        └─ Plug-in PreValidation: chặn bind key đã dùng / hết hạn
             └─ Ops duyệt (Power Automate approval)
                  └─ Custom Connector → POST /internal/v1/orgs/provision (DASHBOARD)
                       └─ DASHBOARD tạo Org + admin user đầu tiên
                            └─ Plug-in PostOperation (async): ghi nx_activation, status = Active
                                 └─ Email onboarding → khách login tại 4201/{orgAlias}/login
```

---

## Bắt đầu

| Bạn đang ở đâu | Đọc file nào |
|---|---|
| Chưa có environment | [docs/P0-SETUP.md](docs/P0-SETUP.md) |
| Xem đặc tả data model | [docs/DATA-MODEL.md](docs/DATA-MODEL.md) (nguồn sự thật) |
| Xem lại cách làm P1 | [docs/P1-DATA-MODEL.md](docs/P1-DATA-MODEL.md) |
| **Làm tiếp → P2 plug-in** | `C:\DEV\docs\POWER-PLATFORM-INTEGRATION.md` §2.3 + [CLAUDE.md](CLAUDE.md) ← **đang ở đây** |

**Environment:** `Nexus-Dev` (Developer, Dataverse ✅, Dynamics 365 apps: No). URL + GUID ở [docs/ENVIRONMENT.local.md](docs/ENVIRONMENT.local.md) — không commit.

**Verify schema bất cứ lúc nào:**

```powershell
.\scripts\verify-schema.ps1     # 5 bang + date behavior + cascade + alternate key
```

Các lệnh CLI hay dùng:

```powershell
pac auth list                      # xem profile
pac auth select --index 0
pac org who                        # xác nhận đang trỏ đúng environment
.\scripts\export-solution.ps1      # export + unpack về solution/
```

---

## Trạng thái

| Giai đoạn | Nội dung | Trạng thái |
|---|---|---|
| P0 | pac CLI + khung repo + environment | ✅ Xong — env `Nexus-Dev` + Dataverse, pac auth profile `NexusDev` |
| P1 | Dataverse solution + 5 table + model-driven app | ✅ **Xong 2026-09-21** — 5 bảng, 4 relationship Restrict, alternate key `nx_UniqueLicenseKey`, app 5 page. Solution đã unpack vào git |
| P2 | Plug-in sinh key + validate binding | 🔄 **Code xong 2026-09-21** — 3 plug-in build sạch, còn lại: đăng ký step + image. Xem [docs/P2-PLUGINS.md](docs/P2-PLUGINS.md) |
| P3 | 2 endpoint DASHBOARD (`orgs/provision`, `orgs/{alias}/usage`) | ⬜ Chưa |
| P4 | Dev Tunnel + Custom Connector + Flow approval | ⬜ Chưa |
| P5 | PCF controls | ⬜ Chưa |
| P6 | ALM pipeline (GitHub Actions) | ⬜ Chưa |
| P7 | Event push DASHBOARD → Flow, utilization report | ⬜ Chưa |

---

## Tài liệu liên quan

| File | Nội dung |
|---|---|
| `C:\DEV\docs\POWER-PLATFORM-INTEGRATION.md` | Phân tích tổng thể + lộ trình P0–P7 |
| `C:\DEV\docs\DASHBOARD-INTERNAL-API.md` | Contract `/internal/v1/*`, service-token auth |
| `C:\DEV\docs\INFRA-NOTES.md` | Port, tài khoản, token của DASHBOARD + HUB |
| `C:\DEV\DASHBOARD\.claude\plans\org-multitenancy.md` | Nghiệp vụ gốc: §5 License mechanism, §8.1 Create Org |
