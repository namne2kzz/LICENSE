# P2 — Plug-in C# (Dataverse)

> Project: `src/plugins/Nexus.Licensing.Plugins` · `net462` · signed (`.snk`)
> Cập nhật: 2026-09-22

## Tiến độ

| Việc | Trạng thái |
|---|---|
| `pac plugin init` + 3 file plug-in | ✅ Xong — `dotnet build` 0 warning, 0 error |
| Đăng ký assembly (PRT, Sandbox/Database) | ✅ Xong — *1 Assembly · 3 Plugin(s) Registered · 0 Errors* |
| Step 1 `GenerateLicenseKeyPlugin` — Create/PreOperation/Sync | ✅ Xong |
| Step 2 `ValidateLicenseBindingPlugin` — Update/PreValidation/Sync | ✅ Xong |
| Step 3 `WriteActivationAuditPlugin` — Update/PostOperation/**Async** | ✅ Xong |
| Image cho step 2 (`PreImage`) | ✅ Xong |
| Image cho step 3 (`PreImage` + `PostImage`) | ✅ Xong |
| Form 5 bảng (đủ cột, read-only cột plug-in ghi) | ✅ Xong |
| Kiểm thử ca 1–2 (GenerateLicenseKeyPlugin) | ✅ PASS |
| **Kiểm thử ca 3–5** | ⬜ **Đang làm** |

---

## 3 plug-in và cấu hình đăng ký

| # | Class | Message | Table | Stage | Mode | Filtering attributes | Images |
|---|---|---|---|---|---|---|---|
| 1 | `GenerateLicenseKeyPlugin` | `Create` | `nx_licensekey` | **PreOperation** (20) | Sync | — | — |
| 2 | `ValidateLicenseBindingPlugin` | `Update` | `nx_orgrequest` | **PreValidation** (10) | Sync | `nx_provisionstate` | **PreImage** |
| 3 | `WriteActivationAuditPlugin` | `Update` | `nx_licensekey` | **PostOperation** (40) | **Async** | `nx_status` | **PreImage** + **PostImage** |

### Vì sao mỗi cái một stage khác nhau

| Stage | Đặc điểm | Dùng khi |
|---|---|---|
| **PreValidation** (10) | Chạy **ngoài** transaction | Validate có thể fail — từ chối sớm, không tốn rollback |
| **PreOperation** (20) | Trong transaction, **trước** khi ghi DB | Sửa `Target` — chỉ cần gán, không gọi Update |
| **PostOperation** (40) | Trong transaction, **sau** khi ghi DB | Cần chắc thao tác đã thành công (ghi audit, tạo bản ghi liên quan) |

**Sync vs Async:** async chạy nền qua system job — không bắt người dùng chờ, và lỗi **không** rollback thao tác chính. Đúng cho audit; sai cho validate.

---

## Cấu hình image chi tiết

Image là bản chụp record. Cần vì **`Target` khi Update chỉ chứa cột bị đổi**.

### `ValidateLicenseBindingPlugin` — PreImage

| Mục | Giá trị |
|---|---|
| Name / Entity Alias | `PreImage` |
| Parameters | `nx_licensekey`, `nx_orgalias` |

Lý do: khi Ops đổi `nx_provisionstate` sang Approved, `Target` **chỉ có** cột đó. License key và alias phải lấy từ image.

### `WriteActivationAuditPlugin` — PreImage + PostImage

| Mục | PreImage | PostImage |
|---|---|---|
| Name / Entity Alias | `PreImage` | `PostImage` |
| Parameters | `nx_status`, `nx_key`, `nx_boundorgalias` | `nx_status`, `nx_key`, `nx_boundorgalias` |

Lý do: cần **cả hai** để biết `from → to`. PreImage cho trạng thái cũ, PostImage cho trạng thái mới đã lưu.

> ⚠️ Không đăng ký image → plug-in 2 và 3 **không chạy đúng**. Plug-in 3 đã có guard: thiếu image thì Trace rồi bỏ qua, không ghi audit lỗi.

---

## Nghiệp vụ từng plug-in

### 1. `GenerateLicenseKeyPlugin`

Khi Sales tạo license key và chọn plan:

```
đọc nx_licenseplan
  → expireDate = UtcNow.Date + durationMonths
  → dueDate    = expireDate - graceDays
  → nx_key     = base64("NEX-{planCode}-{random16}")
  → nx_status  = Issued
  → nx_repocapacity = plan.repoCapacity  (chỉ khi user chưa tự nhập)
```

Chặn: plan chưa chọn · plan `nx_isactive = false`.

### 2. `ValidateLicenseBindingPlugin`

Khi `nx_provisionstate` → `Approved`, kiểm tra 4 invariant:

| # | Invariant | Thông báo khi vi phạm |
|---|---|---|
| 1 | Key phải `Issued` | "Only an Issued license key can be approved…" |
| 2 | `nx_expiredate >= hôm nay` | "This license key expired on…" |
| 3 | `nx_boundorgalias` rỗng | "…already bound to organization '{alias}'" |
| 4 | Alias chưa bị key khác chiếm | "…already bound to another license key" |

Invariant 3 + 4 là bản Dataverse của `HasIndex(o => o.LicenseKey).IsUnique()` bên DASHBOARD.

> 📌 **Vì sao không dùng alternate key cho invariant 4:** `nx_boundorgalias` rỗng khi key ở trạng thái Draft/Issued, mà alternate key Dataverse **không cho nhiều dòng rỗng**. Phải enforce bằng plug-in.

### 3. `WriteActivationAuditPlugin`

Khi `nx_status` đổi → tạo `nx_activation`:

| Status mới | Action ghi nhận |
|---|---|
| Issued | `Issue` |
| Active | `Activate` |
| Revoked | `Revoke` |
| Expired | `Expire` |
| Draft | *(bỏ qua — điểm xuất phát, không phải sự kiện)* |

`nx_fromstatus`/`nx_tostatus` lưu dạng **text** để lịch sử giữ nguyên nhãn tại thời điểm đó, kể cả khi optionset bị sửa về sau.

---

## Quy tắc code đã áp dụng

| Quy tắc | Vì sao |
|---|---|
| `PluginUserService` cho read/write hệ thống | Sales có thể không có quyền đọc `nx_licenseplan`, hoặc tạo `nx_activation` |
| `InitiatingUserId` cho "ai thực hiện" | Audit phải ghi người thật, không phải service account |
| `context.Depth > 1` → return | Chặn vòng lặp vô hạn (Dataverse cắt ở depth 8 với lỗi khó hiểu) |
| `DateTime.UtcNow`, **không** `DateTime.Now` | Sandbox chạy ở múi giờ datacenter Microsoft |
| `.Date` cho cột Date Only | Tránh lệch một ngày do phần giờ |
| `GetAttributeValue<T>` thay vì `entity["..."]` | Trả `default(T)` thay vì ném `KeyNotFoundException` |
| Kiểm tra `Target` + `LogicalName` đầu mỗi plug-in | Step có thể bị đăng ký nhầm bảng |
| `InvalidPluginExecutionException` cho lỗi nghiệp vụ | Message hiện thẳng cho người dùng, không phải stack trace |

### ⚠️ Ba khái niệm "user" dễ nhầm

| | Là ai | Dùng khi |
|---|---|---|
| **`InitiatingUserId`** | Người **bấm nút** | Muốn biết *ai đã làm* — ghi vào audit |
| **`UserId`** | Tài khoản step **chạy dưới quyền** (cấu hình lúc register: "Calling User" hoặc service account) | Hiếm khi cần trực tiếp |
| **`PluginUserService`** | `IOrganizationService` chạy theo `UserId` — **bỏ qua quyền người dùng** | Đọc/ghi dữ liệu hệ thống |
| **`InitiatingUserService`** | `IOrganizationService` chạy theo `InitiatingUserId` — **tôn trọng security role** | Muốn thao tác bị chặn nếu user không có quyền |

**Hai trục độc lập:** *"ai gây ra"* (id) và *"chạy bằng quyền ai"* (service). Trong `ResolveActor`
ta dùng **`InitiatingUserId`** (đúng người) nhưng tra cứu qua **`PluginUserService`** (đủ quyền
đọc bảng `systemuser`) — kết hợp hai trục khác nhau, và đó là cách dùng đúng.

Nếu Ops chạy qua một Flow tự động: `InitiatingUserId` vẫn là Ops, còn `UserId` có thể là service
account của Flow.

### ⚠️ Optionset là **global**, giá trị bắt đầu từ 0

Kiểm tra trong `solution/NexusLicensing/OptionSets/nx_draft.xml`:

```xml
<optionset Name="nx_draft" IsGlobal="1">
  <option value="0"> Draft
  <option value="1"> Issued
```

**Không phải `100000000`** như optionset local. Viết sai sẽ set nhầm trạng thái mà **không crash** — lỗi âm thầm. Mọi giá trị đã gom vào `LicensingConstants.cs`.

---

## Build & deploy

```powershell
cd src\plugins\Nexus.Licensing.Plugins
dotnet build                    # ra bin\Debug\net462\Nexus.Licensing.Plugins.dll

pac tool list                   # xem tool đã cài chưa
pac tool prt                    # Plugin Registration Tool — lần đầu tải ~100MB
```

> ⚠️ `pac tool prt` **chặn terminal** trong lúc PRT mở. Chạy trong cửa sổ riêng:
> ```powershell
> Start-Process powershell -ArgumentList '-NoExit','-Command','pac tool prt'
> ```

> ℹ️ `pac plugin push` cần `--pluginId` (assembly phải có sẵn trên server) nên **lần đầu bắt buộc
> dùng PRT**. Sau đó mới push được bản build mới. `pac` cũng không tạo được step hay image.

---

## Nhật ký đăng ký lần đầu (2026-09-22)

Ghi lại đúng những gì đã gặp, kể cả chỗ vướng.

### Bước 0 — MFA chặn PRT ⚠️ mất nhiều thời gian nhất

PRT hiện dialog **"Unsupported browser — Keep your account secure"** vì tenant bắt buộc thiết lập
MFA, mà PRT dùng trình duyệt nhúng IE-based.

Đã xử lý bằng cách thêm phương thức **Microsoft Authenticator** (one-time code) — chi tiết ở
[P0-SETUP.md](P0-SETUP.md#-mfa-prt-không-dùng-được-passkey).

### Bước 1 — Kết nối

**Create New Connection** → Deployment Type **Office 365** (mặc định là *On-premises*, phải đổi)
→ ☑ Display list of available organizations → User Name + Password → Login → chọn **Nexus-Dev**.

Thành công: tab `Nexus-Dev` xuất hiện, cây liệt kê assembly hệ thống của Microsoft.

### Bước 2 — Register New Assembly

`Register ▾` → **Register New Assembly** (`Ctrl+A`):

| Step | Giá trị |
|---|---|
| 1. Location | `C:\DEV\LICENSE\src\plugins\Nexus.Licensing.Plugins\bin\Debug\net462\Nexus.Licensing.Plugins.dll` → **Load Assembly** |
| 2. Select plugins | tích cả 3 — phải thấy hậu tố **`- Isolatable`** |
| 3. Isolation mode | **Sandbox** |
| 4. Location | **Database** |
| 5. | **Register Selected Plugins** |

> ✅ **`- Isolatable`** là dấu hiệu assembly đã ký strong name và target framework đúng. Không thấy
> chữ đó nghĩa là DLL có vấn đề — kiểm tra `.snk` và `net462`.

Kết quả mong đợi: *1 Assembly Registered · 3 Plugin(s) Registered · 0 Encountered Errors*.

### Bước 3 — Register New Step (×3)

Chọn **class** trong cây → chuột phải → **Register New Step**. Điền theo bảng ở đầu tài liệu.

Các field giữ nguyên mặc định ở cả 3 step: Secondary Entity *(trống)* · Run in User's Context
`Calling User` · Execution Order `1` · Deployment ☑ Server · Step Name và Description PRT tự sinh.

> 🔴 **Chỗ sai nhiều nhất: Event Pipeline Stage mặc định là `PreValidation`.** Chỉ step 2 dùng giá
> trị đó; step 1 và 3 phải đổi. Ba step **khác nhau cả stage lẫn mode**:
>
> | | Step 1 | Step 2 | Step 3 |
> |---|---|---|---|
> | Stage | **PreOperation** | PreValidation *(mặc định)* | **PostOperation** |
> | Mode | Synchronous | Synchronous | **Asynchronous** |

> 💡 **Filtering Attributes**: chữ xám = *placeholder*, chưa chọn gì. Phải bấm nút **`...`** mở
> dialog và tích cột. Chọn xong chữ chuyển **đen đậm**. Bỏ trống = chạy mỗi lần bất kỳ cột nào đổi.

> 💡 Với step 3 (Asynchronous), checkbox **"Delete AsyncOperation if StatusCode = Successful"**
> **để trống** — giữ system job lại để còn xem được lỗi khi plug-in fail.

### Bước 4 — Register New Image

Chọn **step** (không phải class) → chuột phải → **Register New Image**. Xem mục
"Cấu hình image chi tiết" ở trên.

---

## Debug

Không attach debugger vào sandbox được. Công cụ duy nhất là **Plugin Trace Log**:

1. Bật: **Power Platform admin center → Environment → Settings → Product → Privacy + Security → Plug-in trace log = All**
2. Xem: trong model-driven app, đổi URL thành `.../main.aspx?etn=plugintracelog&pagetype=entitylist`

Mọi `localPluginContext.Trace(...)` trong code sẽ hiện ở đó, kèm CorrelationId và thời gian thực thi.

> ⚠️ Nhớ **tắt lại** sau khi debug xong — trace log ghi rất nhiều và chiếm dung lượng Dataverse.

---

## Kiểm thử thủ công (sau khi đăng ký)

| # | Thao tác | Kết quả mong đợi |
|---|---|---|
| 1 | Tạo License Key, chọn plan `STD` | `nx_key` tự sinh · status `Issued` · expire = hôm nay + 12 tháng · due = expire − 15 ngày · capacity = 10 |
| 2 | Tạo License Key không chọn plan | Chặn: "Select a License Plan…" |
| 3 | Tạo Org Request → đổi state sang `Approved` | Qua được nếu key `Issued` và alias chưa dùng |
| 4 | Approve request thứ hai dùng **cùng key** | Chặn: "…already bound to organization…" |
| 5 | Đổi status key sang `Revoked` | Sinh bản ghi `nx_activation` action `Revoke`, from `Active` to `Revoked` |

Sau bước 1, verify bằng CLI:

```powershell
pac env fetch --xmlFile .\scripts\fetch\licensekeys.xml
```
