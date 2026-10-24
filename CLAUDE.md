# NKey — License service (Power Platform) — Claude Instructions

> Onboarding & license cho **DASHBOARD** + **HUB**, xây trên **Power Platform / Dataverse**.
> Repo riêng (`C:\DEV\LICENSE`), cùng mô hình tách service với HUB. Đọc `README.md` để biết vì sao service này tồn tại.
>
> **Tên sản phẩm: NKey.** Định danh kỹ thuật trên Dataverse giữ nguyên (không đổi được sau khi tạo):
> solution `NexusLicensing` · prefix `nx_` · namespace `Nexus.Licensing.Plugins`.
> Dùng "NKey" ở văn xuôi/UI; dùng tên kỹ thuật khi nói về solution, bảng, assembly.

## Stack

| Layer | Tech |
|-------|------|
| Data & App | **Dataverse** + **Model-driven app** (solution `NexusLicensing`, publisher prefix `nx`) |
| Business logic | **Plug-in C#** (.NET Framework 4.6.2, `Microsoft.CrmSdk.CoreAssemblies`) |
| Workflow | **Power Automate** (approval + gọi DASHBOARD) |
| UI component | **PCF** (TypeScript, vanilla/React — **KHÔNG Angular**) |
| Tích hợp | **Custom Connector** → DASHBOARD `/internal/v1/*` (header `X-Internal-Token`) |
| ALM | **pac CLI** + GitHub Actions (`microsoft/powerplatform-actions`) |

---

## Hard Constraints

### Ranh giới hệ thống (never violate)
- **Dataverse là source of truth của license.** DASHBOARD chỉ giữ snapshot read-only (`Organization.LicenseKey/LicenseDueDate/LicenseExpireDate/LicenseRepoCapacity`). Không viết code cho phép sửa license từ phía DASHBOARD.
- **LICENSE → DASHBOARD** chỉ qua REST `/internal/v1/*`. **Không** đọc DB DASHBOARD, không nghe broker của DASHBOARD/HUB.
- **Không** đưa nghiệp vụ create-org ngược lại vào DASHBOARD — đã chốt gỡ (2026-08-23), đừng vô tình tái tạo.

### Plug-in rules (non-negotiable)
- Target **.NET Framework 4.6.2** — KHÔNG phải .NET 10. Xem "Vì sao net462" bên dưới.
- **Không async/await** trong plug-in. Không `Task`, không `HttpClient` trong sync plug-in — gọi ngoài đẩy sang Power Automate hoặc async plug-in.
- Constructor chỉ được nhận `()` hoặc `(string unsecureConfig, string secureConfig)`. **Không có DI container** — lấy service qua `IServiceProvider`.
- Plug-in phải **stateless** (Dataverse cache instance). Không field mutable ở class level.
- Luôn kiểm tra `context.Depth` để tránh vòng lặp vô hạn.
- Ném `InvalidPluginExecutionException` để báo lỗi nghiệp vụ cho người dùng — không để exception thô lọt ra.
- Assembly phải **ký tên** (strong name, `.snk`) mới register được.
- **`.snk` ĐƯỢC commit** — theo khuyến nghị Microsoft. Strong name không phải cơ chế bảo mật mà là
  **định danh assembly**: đổi key → Dataverse coi là assembly khác → phải đăng ký lại mọi step và
  image. Giữ trong repo để mọi máy build ra cùng một identity.

### 🔒 Không commit định danh tenant/environment

Tenant name, email admin, Environment URL và các GUID (Org ID, Environment ID, publisher id,
solution id) **không được commit** — chúng cho người lạ biết điểm đăng nhập và tài khoản để dò.

Giá trị thật ở `docs/ENVIRONMENT.local.md` (gitignore). Trong tài liệu và lệnh, dùng placeholder
`<Environment URL>` / `<tên>.onmicrosoft.com` và trỏ về file local.

#### Vì sao `net462` mà không phải .NET mới hơn

Plug-in **không chạy trong process của bạn** — nó chạy trong **sandbox process của Dataverse** trên
máy chủ Microsoft. Ta chỉ upload assembly DLL; runtime là của họ, và sandbox đó chạy .NET Framework
4.6.2. Đây là ràng buộc của môi trường host, không phải lựa chọn. Upload DLL target net8/net10 sẽ bị
từ chối lúc register.

Xác nhận từ template thật: `pac plugin init` (v2.12.2) sinh csproj `<TargetFramework>net462</TargetFramework>`
+ `Microsoft.CrmSdk.CoreAssemblies 9.0.2.*`.

> 💡 **`net462` giới hạn runtime API, KHÔNG giới hạn cú pháp ngôn ngữ.** Đặt
> `<LangVersion>latest</LangVersion>` là dùng được primary constructor, collection expression `[]`,
> pattern matching mới, file-scoped namespace, `record`… Bật `<Nullable>enable</Nullable>` cũng được.
> Cái mất là BCL mới: không `System.Text.Json` (dùng Newtonsoft — đã có sẵn trong package plug-in),
> không `Span<T>` đầy đủ.

**Nếu thật sự cần .NET mới:** kiến trúc plug-in mỏng + service ngoài (plug-in net462 tối thiểu → gọi
Azure Function/API .NET 10 chứa logic). **Không dùng cho dự án này** — plug-in của ta chỉ sinh key,
validate invariant, ghi audit; đẩy ra ngoài sẽ mất tính nguyên tử của transaction Dataverse và thêm
một điểm hỏng.

### PCF rules
- Lifecycle: `init` → `updateView` → `getOutputs` → `destroy`. Không có change detection tự động — tự quản lý render.
- Gọi data qua `context.webAPI`, không `fetch` thẳng.
- Dọn tài nguyên trong `destroy` (event listener, timer).

### Solution & ALM rules
- **Commit bản unpack**, KHÔNG commit `.zip` (đã gitignore).
- Dev = **unmanaged**. Test/Prod = **managed**. Không bao giờ import unmanaged vào Test/Prod.
- URL DASHBOARD theo môi trường → dùng **environment variable** của solution, không hardcode.
- Kết nối connector → dùng **connection reference**, không bind cứng connection.
- Mọi thay đổi làm trên cloud phải `pac solution export` + `unpack` về repo trước khi kết thúc buổi làm.

### Naming
- Publisher prefix: `nx`. Table: `nx_customer`, `nx_licensekey`, `nx_orgrequest`, `nx_activation`.
- Plug-in namespace: `Nexus.Licensing.Plugins`. Class đặt tên `<Hành động>Plugin` (vd `GenerateLicenseKeyPlugin`).
- PCF namespace: `NexusLicensing`.

---

## Code documentation — every public member

```csharp
/// <summary>One-line description.</summary>
/// <param name="serviceProvider">Dataverse service provider.</param>
/// <exception cref="InvalidPluginExecutionException">Khi vi phạm invariant nghiệp vụ.</exception>
```

```typescript
/** One-line description. @param context PCF context. @returns void. */
```

---

## Lệnh hay dùng

### Cài đặt tooling (máy mới)

```powershell
# Power Platform CLI — cài như dotnet global tool
dotnet tool install --global Microsoft.PowerApps.CLI.Tool
dotnet tool update  --global Microsoft.PowerApps.CLI.Tool   # nâng cấp

# LƯU Ý: `pac --version` KHÔNG phải lệnh hợp lệ. Version in ở banner của:
pac help
```

Tuỳ chọn — module PowerShell thao tác dữ liệu Dataverse (cần NuGet provider trước,
và `Connect-CrmOnline` mở cửa sổ đăng nhập nên **không chạy được trong phiên non-interactive**):

```powershell
Install-PackageProvider -Name NuGet -MinimumVersion 2.8.5.201 -Force -Scope CurrentUser
Set-PSRepository -Name PSGallery -InstallationPolicy Trusted
Install-Module Microsoft.Xrm.Data.PowerShell -Scope CurrentUser -Force -AllowClobber
```

### Kết nối environment

```powershell
pac auth create --name NexusDev --environment <Environment URL>   # xem docs/ENVIRONMENT.local.md
pac auth list                                   # xem profile
pac auth select --name NexusDev
pac org who                                     # xác nhận environment đang trỏ
```

### Solution

```powershell
.\scripts\export-solution.ps1                   # export + unpack về solution/
.\scripts\import-solution.ps1 -Profile NexusTest -Managed
.\scripts\verify-schema.ps1                     # bảng + date behavior + cascade + alternate key
.\scripts\verify-licenseplan.ps1                # Min/Max/MaxLength của nx_licenseplan

pac solution list                               # solution trên environment
```

### Truy vấn dữ liệu (FetchXML)

```powershell
pac env fetch --xmlFile .\scripts\fetch\licenseplans.xml
pac env fetch --xmlFile .\scripts\fetch\licensekeys.xml
pac env fetch --xmlFile .\scripts\fetch\activations.xml
```

> ⚠️ FetchXML **phải để trong file** và dùng `--xmlFile`. Truyền XML inline vào PowerShell bị
> nuốt quote → `System.Xml.XmlException`.
>
> ⚠️ Không dùng thuộc tính `top="N"` trong fetch — `pac` tự thêm paging và sẽ báo
> *"The top attribute can't be specified with paging attribute page"*.

### Plug-in

```powershell
pac plugin init --outputDirectory src\plugins\Nexus.Licensing.Plugins --author "Nam Phuong Huynh"
cd src\plugins\Nexus.Licensing.Plugins; dotnet build

pac tool list                                   # xem tool đã cài chưa
pac tool prt                                    # Plugin Registration Tool (lần đầu tải ~100MB)
```

> ℹ️ `pac plugin push` cần `--pluginId` (assembly phải có sẵn trên server) nên **lần đầu vẫn
> phải đăng ký bằng PRT**. Sau đó mới push bản build mới lên được.
>
> ℹ️ `pac` **không** tạo được publisher / solution / table / column mới trên server, cũng không
> ghi dữ liệu. Chỉ import/export solution, query đọc (`env fetch`), và sinh code
> (`modelbuilder build`). Những việc còn lại làm trên Maker portal hoặc PRT.

### PCF

```powershell
pac pcf init --namespace NexusLicensing --name CapacityGauge --template field
```

---

## Cấu hình đăng ký plug-in step

DLL: `src\plugins\Nexus.Licensing.Plugins\bin\Debug\net462\Nexus.Licensing.Plugins.dll`
Assembly: Isolation **Sandbox** · Location **Database**

| Class | Message | Table | Stage | Mode | Filtering attributes | Images |
|---|---|---|---|---|---|---|
| `GenerateLicenseKeyPlugin` | `Create` | `nx_licensekey` | PreOperation | Sync | — | — |
| `ValidateLicenseBindingPlugin` | `Update` | `nx_orgrequest` | PreValidation | Sync | `nx_provisionstate` | `PreImage` |
| `WriteActivationAuditPlugin` | `Update` | `nx_licensekey` | PostOperation | **Async** | `nx_status` | `PreImage` + `PostImage` |

Parameters của image:

| Step | Image | Parameters |
|---|---|---|
| `ValidateLicenseBindingPlugin` | `PreImage` | `nx_licensekey`, `nx_orgalias` |
| `WriteActivationAuditPlugin` | `PreImage` và `PostImage` | `nx_status`, `nx_key`, `nx_boundorgalias` |

> ⚠️ Tên image phải **đúng** `PreImage` / `PostImage` — code đọc theo tên này. Thiếu image thì
> plug-in 2 và 3 không chạy đúng.

Chi tiết + 5 ca kiểm thử: `docs/P2-PLUGINS.md`.

> ⚠️ **PRT không dùng được Passkey** (trình duyệt nhúng IE-based, không hỗ trợ WebAuthn).
> Tenant này đăng nhập PRT bằng **Microsoft Authenticator one-time code** — xem `docs/P0-SETUP.md`.

### Debug plug-in

Không attach debugger vào sandbox được. Công cụ duy nhất là **Plugin Trace Log**:

1. Bật: Power Platform admin center → Environment → Settings → Product → Privacy + Security →
   **Plug-in trace log = All**
2. Xem: trong model-driven app đổi URL thành `...?etn=plugintracelog&pagetype=entitylist`
3. **Tắt lại sau khi debug** — trace log ghi rất nhiều, tốn dung lượng Dataverse

---

## Tài liệu tham chiếu

| File | Nội dung |
|---|---|
| `C:\DEV\docs\POWER-PLATFORM-INTEGRATION.md` | Phân tích tổng thể, data model §2.2, plug-in §2.3, lộ trình P0–P7 |
| `C:\DEV\docs\DASHBOARD-INTERNAL-API.md` | Contract `/internal/v1/*`, `ServiceTokenAuthenticationHandler` |
| `C:\DEV\docs\INFRA-NOTES.md` | Port, tài khoản, `X-Internal-Token` (⚠️ đang là placeholder, phải đổi trước khi expose qua tunnel) |
| `C:\DEV\DASHBOARD\.claude\plans\org-multitenancy.md` | §5 License mechanism, §8.1 luồng Create Org |
| `docs/DATA-MODEL.md` | **Đặc tả bảng/cột — nguồn sự thật**; §10 ghi các bẫy date behavior đã gặp |
| `docs/P0-SETUP.md` | Checklist dựng environment |
| `docs/P1-DATA-MODEL.md` | Quy trình tạo bảng trên portal; cảnh báo canvas editor |
| `docs/P2-PLUGINS.md` | Đăng ký step/image, quy tắc code plug-in, ca kiểm thử |

---

## Khi sửa nghiệp vụ

Repo này chưa có thư mục `.claude/histories/`. Khi P1 xong và có nghiệp vụ thật, tạo `docs/business/{feature}.dod.md` theo tinh thần `C:\DEV\DASHBOARD\.claude\histories\RULES.md`.
