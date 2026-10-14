# P0 — Môi trường Power Platform

> ✅ **Hoàn tất 2026-09-20.** Đây là ghi chép cách đã dựng, không phải checklist dự kiến.

---

## Thông tin môi trường

> 🔒 Tenant name, email admin, environment URL và các GUID **không commit** — chúng cho người lạ
> biết điểm đăng nhập và tài khoản để dò.
>
> Giá trị thật ở **`docs/ENVIRONMENT.local.md`** (đã gitignore).
> Mẫu: [ENVIRONMENT.local.example.md](ENVIRONMENT.local.example.md).

Cấu hình đã chọn (phần không nhạy cảm):

| Mục | Giá trị |
|---|---|
| Environment | `Nexus-Dev`, type **Developer** |
| Dynamics 365 apps | `No` (**irreversible** — chỉ cần Dataverse thuần) |
| Language / Currency | English (United States) / VND |
| MFA | Microsoft Authenticator (one-time code) — ⚠️ Passkey không dùng được với PRT |
| pac auth profile | `NexusDev` |
| Publisher | `Nexus` / prefix `nx` |
| Solution | `NexusLicensing` v1.0.0.0 |

> P6 tạo `Nexus-Test` phải chọn **cùng Region** với `Nexus-Dev` — xem file local.

---

## Tooling trên máy

| Mục | Phiên bản |
|---|---|
| .NET SDK | 10.0.301 |
| Node.js / npm | v24.15.0 / 11.17.0 |
| Power Platform CLI | 2.12.2 |
| Visual Studio | Community 2026 (18.7.1) |

```powershell
dotnet tool install --global Microsoft.PowerApps.CLI.Tool
pac help    # version in ở banner — `pac --version` KHÔNG phải lệnh hợp lệ
```

---

## Cách đã dựng tenant

Email cá nhân (`outlook.com`) **không** đăng ký Power Apps Developer Plan được — Microsoft yêu cầu
tài khoản work/school. Mà work/school account không phải thứ có sẵn: nó là **kết quả** của việc
đăng ký một tenant.

```
email cá nhân  ──đăng ký M365 trial──►  tenant <tên>.onmicrosoft.com
   (email liên hệ)                              │
                                                ▼
                            admin@<tenant>.onmicrosoft.com
                               (work/school account, Global Admin)
                                                │
                                                ▼
                                Power Apps Developer Plan ✅
```

Các bước đã làm:

1. Đăng ký **Microsoft 365 Business Basic trial** → đặt tên tenant → tạo tài khoản admin
2. **Tắt recurring billing** ngay: Admin center → Billing → Your products
3. https://aka.ms/PowerAppsDevPlan → đăng nhập bằng tài khoản `.onmicrosoft.com` → **Start free**

> Trial hết 30 ngày không ảnh hưởng — Developer Plan là gói riêng, miễn phí vĩnh viễn, tenant và
> environment vẫn còn.

---

## Cách đã tạo environment

Tenant có sẵn env `Nexus (default)` nhưng **không dùng**: `Dataverse = No`, mọi user đều tạo app
được, không xoá/đổi tên được, không hợp pipeline ALM ở P6.

1. https://admin.powerplatform.microsoft.com → **Environments** → **+ New**
2. Name `Nexus-Dev` · Type **Developer** · Region `crm7`
3. Env tạo ra vẫn `Dataverse = No` → chọn env → **Add Dataverse**:

| Field | Giá trị |
|---|---|
| Language | English (United States) |
| Currency | VND |
| Enable Dynamics 365 apps | **No** — ⚠️ *irreversible*, muốn đổi phải tạo env mới |
| Deploy sample apps and data | No |

> `Nexus-Test` **không thuộc P0** — tạo ở P6. Developer Plan giới hạn 3 environment và env bỏ
> không dùng lâu có thể bị Microsoft tự dọn.

---

## Kết nối `pac`

```powershell
pac auth create --name NexusDev --environment <Environment URL>   # xem ENVIRONMENT.local.md
pac org who     # phải in ra Nexus-Dev
```

---

## ⚠️ MFA: PRT không dùng được Passkey

Tenant mới bật **Security Defaults** → bắt buộc thiết lập MFA. Plugin Registration Tool dùng trình
duyệt nhúng **IE-based**, **không hỗ trợ WebAuthn** → mọi phương thức **Passkey đều không đăng nhập
được**, dù thiết lập thành công.

Trang `aka.ms/mysecurityinfo` mặc định **chỉ hiện 2 lựa chọn Passkey**. Phải bấm link
**`Other options`** mới ra danh sách đầy đủ.

**Đã chọn: Microsoft Authenticator** *(Approve sign-in requests **or use one-time codes**)* — quét
QR bằng app điện thoại. Khi login PRT, nếu push notification bị treo thì chọn
*"I can't use my Microsoft Authenticator app right now"* → **"Use a verification code"** → nhập mã
6 số từ app.

---

## Vướng đã gặp

| Triệu chứng | Nguyên nhân | Xử lý |
|---|---|---|
| "You need a work or school account" | Dùng email cá nhân | Tạo tenant riêng qua M365 trial |
| Cột `Dataverse` = `No` sau khi tạo env | Toggle "Add a Dataverse data store" không bật | Env kiểu Developer thêm được sau bằng **Add Dataverse** |
| PRT: *"Unsupported browser — Keep your account secure"* | Tenant ép MFA, PRT không chạy được trang thiết lập | Thiết lập MFA trên trình duyệt thật trước |
| `mysecurityinfo` chỉ hiện Passkey | Trang mặc định rút gọn | Bấm **`Other options`** |
| `pac org who` báo 401 | Token hết hạn | `pac auth clear` rồi `pac auth create` lại |

---

## → P1

[P1-DATA-MODEL.md](P1-DATA-MODEL.md) — quy trình tạo bảng
[DATA-MODEL.md](DATA-MODEL.md) — đặc tả bảng/cột

---

## 💰 Đã huỷ M365 trial (2026-10-14) — NKey không ảnh hưởng

M365 Business Basic trial đăng ký 2026-09-20 chỉ để **lấy work/school account**, vì email cá nhân
không đăng ký được Power Apps Developer Plan.

**Đã huỷ ngày 14/10**, trước ngày gia hạn **19/10**. Lý do huỷ:

| | |
|---|---|
| Purchased quantity | **25 seats** |
| Pricing model | **Paid** (hiển thị `$0.00` chỉ là giá của kỳ trial đang chạy) |
| Nếu để gia hạn | ~$6/user/tháng × 25 ≈ **$150/tháng** |

> ⚠️ Cột **"Renewal or expiration"** ở Billing → Your products dùng **một ngày cho cả hai nghĩa** —
> không phân biệt được "gia hạn" hay "hết hạn". Phải mở trang chi tiết subscription mới biết
> recurring billing đang On hay Off.

### Đã kiểm chứng sau khi huỷ — mọi thứ còn nguyên

```
pac org who                 → Connected to Nexus-Dev ✅
pac solution list           → NexusLicensing 1.0.0.0 ✅
pac env fetch licenseplans  → 3 bản ghi TRIAL/STD/ENT ✅
.\scripts\verify-schema.ps1 → === P1 HOAN TAT === ✅
```

**Vì sao không ảnh hưởng:**

| Thành phần | Thuộc gói | Sau khi huỷ |
|---|---|---|
| Tài khoản `admin@<tenant>.onmicrosoft.com` | **Microsoft Entra ID Free** (miễn phí vĩnh viễn) | ✅ Còn login được |
| Environment + Dataverse | **Power Apps Developer Plan** (gói riêng, miễn phí) | ✅ Còn |
| 5 bảng, plug-in, model-driven app | Dataverse | ✅ Còn |
| Exchange mail / Teams / SharePoint | M365 Business Basic | ❌ Mất — NKey không dùng |

Business Basic chỉ cấp **quyền dùng dịch vụ M365** cho tài khoản, không phải bản thân tài khoản.
Quyền sửa Dataverse đến từ **security role trong environment** + Developer Plan — độc lập hoàn toàn.

### Nếu sau này cần M365 trở lại

Mua lại được bất cứ lúc nào (Billing → Purchase services), nhưng **không còn trial miễn phí** —
Microsoft chỉ cho một trial cho mỗi sản phẩm trên mỗi tenant. Khi đó mua **1 seat** là đủ, không
phải 25.

Chỉ cần khi muốn gửi/nhận email thật qua tenant, dùng Teams/SharePoint, hoặc test Power Automate
với connector Outlook ở P4 *(có thể thay bằng SMTP hoặc connector khác)*.
