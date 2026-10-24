# Data model — NKey (Dataverse)

> Bản **chuẩn** để tạo table. Rà lại dựa trên nghiệp vụ thật của DASHBOARD
> (`Domain/Entities/Organization.cs`, `OrganizationConfiguration.cs`, `CreateRepositoryCommandHandler.cs`,
> `.claude/plans/org-multitenancy.md` §5).
> Publisher prefix: `nx` · Solution: `NexusLicensing` · Cập nhật: 2026-09-22
>
> ✅ **Đã triển khai đầy đủ trên `Nexus-Dev`.** Tên cột trong tài liệu này khớp với Dataverse thật
> (đối chiếu `solution/NexusLicensing/Entities/*/Entity.xml`). Verify: `.\scripts\verify-schema.ps1`

---

## 0. Kết luận: **5 table**

| # | Table | Vai trò | Bắt buộc ở giai đoạn |
|---|---|---|---|
| 1 | `nx_customer` | Khách hàng (công ty mua license) | P1 |
| 2 | `nx_licensekey` | License key — **aggregate root** | P1 |
| 3 | `nx_orgrequest` | Yêu cầu tạo Org → provision sang DASHBOARD | P1 |
| 4 | `nx_activation` | Audit trail bất biến (issue/activate/renew/revoke) | P1 |
| 5 | `nx_licenseplan` | Gói bán sẵn (Trial/Standard/Enterprise + capacity + thời hạn) | P1 — xem §6 |

> **Vì sao thêm `nx_licenseplan`** (bản phác thảo ban đầu không có): `nx_tier` dạng Choice cứng nghĩa là mỗi lần đổi giá/capacity của gói phải **sửa metadata + deploy solution**. Tách thành table cho phép Sales tự thêm gói mới bằng dữ liệu. Đây cũng là chỗ plug-in `GenerateLicenseKeyPlugin` đọc để **tự điền** `duedate`/`expiredate`/`repocapacity` — biến nó từ plug-in "sinh chuỗi ngẫu nhiên" thành plug-in có nghiệp vụ thật.
>
> **Ranh giới:** đây là phiên bản **1 sản phẩm** (license cho DASHBOARD). Không thêm table `nx_product` — chưa có nhu cầu thật, thêm vào chỉ làm nặng. Ghi nhận ở §7 nếu sau này cần.

**Quan hệ:**

```
nx_customer ──1:N──► nx_licensekey ──1:N──► nx_orgrequest
                          │
                          └──1:N──► nx_activation

nx_licenseplan ──1:N──► nx_licensekey
```

---

## 1. `nx_licenseplan` — Gói license

Dữ liệu cấu hình, Sales/Ops nhập một lần rồi dùng lại.

| Table | Giá trị |
|---|---|
| Display name | `License Plan` · Plural `License Plans` |
| Schema | `nx_licenseplan` |
| Primary column | `Plan Name` → `nx_planname` |

| # | Display name | Name | Type | Required | Cấu hình |
|---|---|---|---|---|---|
| 1 | Plan Name | `nx_planname` | Text (100) | ✅ primary | Trial · Standard · Enterprise |
| 2 | Plan Code | `nx_plancode` | Text (20) | ✅ | `TRIAL`/`STD`/`ENT` — dùng trong license key |
| 3 | Repo Capacity | `nx_repocapacity` | Whole Number | ✅ | Min 1 · Max 1000 |
| 4 | Duration (months) | `nx_durationmonths` | Whole Number | ✅ | Min 1 · Max 120 — tính `expiredate` |
| 5 | Grace Period (days) | `nx_gracedays` | Whole Number | ⬜ | Min 0 · Max 90 — `duedate` → `expiredate`. **Không set default** (solution editor mới không có ô Default cho Whole number); plug-in đọc `?? 0` |
| 6 | Is Active | `nx_isactive` | Yes/No | ✅ | Default **Yes** — gói ngừng bán thì tắt |

> 💡 `nx_durationmonths` + `nx_gracedays` cho phép plug-in tính ngày tự động thay vì Sales gõ tay — bớt sai sót và là nghiệp vụ thật để viết code.

---

## 2. `nx_customer` — Khách hàng

| Table | Giá trị |
|---|---|
| Display name | `Customer` · Plural `Customers` |
| Schema | `nx_customer` |
| Primary column | `Customer Name` → `nx_customername` |

| # | Display name | Name | Type | Required | Cấu hình |
|---|---|---|---|---|---|
| 1 | Customer Name | `nx_customername` | Text (200) | ✅ primary | Khớp `Organization.Name` (max 200) |
| 2 | Contact Email | `nx_contactemail` | Text — format **Email** (320) | ✅ | Khớp `Organization.ContactEmail` (max 320) |
| 3 | Contact Person | `nx_contactperson` | Text (100) | ⬜ | Người liên hệ |
| 4 | Phone | `nx_phone` | Text — format **Phone** (30) | ⬜ | |
| 5 | Tax Code | `nx_taxcode` | Text (30) | ⬜ | MST — xuất hoá đơn |
| 6 | About | `nx_about` | Multiline text (2000) | ⬜ | Khớp `Organization.About` (max 2000) |
| 7 | Country | `nx_country` | Text (100) | ⬜ | |

> ⚠️ Max length **bám đúng DASHBOARD**. `nx_contactemail` để 320 vì `Organization.ContactEmail` là `HasMaxLength(320)` — đặt ngắn hơn sẽ chặn dữ liệu hợp lệ khi provision.

---

## 3. `nx_licensekey` — License key ⭐ aggregate root

Table quan trọng nhất. Mọi invariant nằm ở đây.

| Table | Giá trị |
|---|---|
| Display name | `License Key` · Plural `License Keys` |
| Schema | `nx_licensekey` |
| Primary column | `Key` → `nx_key` |

| # | Display name | Name | Type | Required | Cấu hình |
|---|---|---|---|---|---|
| 1 | Key | `nx_key` | Text (500) | ✅ primary | **base64 opaque**, plug-in sinh. Max 500 khớp `Organization.LicenseKey` |
| 2 | Customer | `nx_customer` | **Lookup** → `nx_customer` | ✅ | |
| 3 | Plan | `nx_plan` | **Lookup** → `nx_licenseplan` | ✅ | Nguồn để plug-in tính dates + capacity |
| 4 | Status | `nx_status` | Choice (**global** `nx_draft`) | ✅ | Draft · Issued · Active · Expired · Revoked — default **Draft** |
| 5 | Due Date | `nx_duedate` | Date only — **Date Only** | ⬜ | Hạn thanh toán. Khớp `Organization.LicenseDueDate` |
| 6 | Expire Date | `nx_expiredate` | Date only — **Date Only** | ⬜ | Khớp `Organization.LicenseExpireDate` |
| 7 | Repo Capacity | `nx_repocapacity` | Whole Number | ⬜ | Min 1 · Max 1000. **Plug-in luôn ghi đè theo plan** — form để read-only. Muốn capacity khác thì tạo plan mới |
| 8 | Bound Org Alias | `nx_boundorgalias` | Text (50) | ⬜ | Khớp `Organization.Alias` (max 50, unique global) |
| 9 | Bound Org Id | `nx_boundorgid` | Text (50) | ⬜ | GUID `Organization.Id` từ DASHBOARD |
| 10 | Activated On | `nx_activatedon` | Date and Time — **User Local** | ⬜ | Thời điểm provision xong |
| 11 | Notes | `nx_notes` | Multiline text (2000) | ⬜ | |

### Choice `Status` — vòng đời

```
Draft ──► Issued ──► Active ──► Expired
             │          │
             └──────────┴──► Revoked
```

| Label | Ý nghĩa | Ai đổi |
|---|---|---|
| Draft | Vừa tạo, chưa sinh key | mặc định |
| Issued | Đã sinh key, gửi khách, chưa dùng | plug-in / Ops |
| Active | Đã bind vào 1 Org ở DASHBOARD | Flow sau provision |
| Expired | Quá `nx_expiredate` | job định kỳ / Flow |
| Revoked | Thu hồi (vi phạm, hoàn tiền) | Ops thủ công |

> 📌 `Status` thay cho flag `active` (true/false) trong plan gốc — vì `false` không phân biệt được "chưa bán" với "đã thu hồi". Ba trạng thái Draft/Issued/Revoked đều là `active=false` ở model cũ.

### Alternate Key — bắt buộc

| Display name | Name | Columns |
|---|---|---|
| `Unique License Key` | `nx_UniqueLicenseKey` | `nx_key` |

Tương đương `builder.HasIndex(o => o.LicenseKey).IsUnique()` bên DASHBOARD.

> ⚠️ **Không** tạo alternate key trên `nx_boundorgalias`: cột này rỗng ở trạng thái Draft/Issued, mà alternate key **không cho phép nhiều dòng rỗng**. Invariant "1 alias ↔ 1 key" enforce bằng plug-in (P2).

---

## 4. `nx_orgrequest` — Yêu cầu tạo Org

Một license key có thể phát sinh nhiều request (lần đầu thất bại, làm lại).

| Table | Giá trị |
|---|---|
| Display name | `Org Request` · Plural `Org Requests` |
| Schema | `nx_orgrequest` |
| Primary column | `Org Name` → `nx_orgname` |

| # | Display name | Name | Type | Required | Cấu hình |
|---|---|---|---|---|---|
| 1 | Org Name | `nx_orgname` | Text (200) | ✅ primary | → `Organization.Name` |
| 2 | Org Alias | `nx_orgalias` | Text (50) | ✅ | → `Organization.Alias`. **URL-safe, unique global** |
| 3 | License Key | `nx_licensekey` | **Lookup** → `nx_licensekey` | ✅ | |
| 4 | Admin Name | `nx_adminname` | Text (100) | ✅ | Admin đầu tiên của org |
| 5 | Admin Email | `nx_adminemail` | Text — format **Email** (320) | ✅ | |
| 6 | Org About | `nx_orgabout` | Multiline text (2000) | ⬜ | → `Organization.About` |
| 7 | Provision State | `nx_provisionstate` | Choice (**global** `nx_orgrequestprovisionstate`) | ✅ | Draft · Approved · Provisioning · Completed · Failed — default **Draft** |
| 8 | Dashboard Org Id | `nx_dashboardorgid` | Text (50) | ⬜ | GUID trả về |
| 9 | Login Url | `nx_loginurl` | Text — format **URL** (500) | ⬜ | `.../{alias}/login` |
| 10 | Requested On | `nx_requestedon` | Date and Time — **User Local** | ⬜ | |
| 11 | Completed On | `nx_completedon` | Date and Time — **User Local** | ⬜ | |
| 12 | Error Message | `nx_errormessage` | Multiline text (2000) | ⬜ | Lỗi khi Failed — Flow ghi |
| 13 | Retry Count | `nx_retrycount` | Whole Number | ⬜ | Min 0 · Max 10 · Default 0 |

### Choice `Provision State`

```
Draft ──► Approved ──► Provisioning ──► Completed
                             │
                             └──► Failed ──► (retry) ──► Provisioning
```

> 💡 `nx_errormessage` + `nx_retrycount` là phần **error handling** của Flow (§2.5 tài liệu tổng) — chỗ hay bị bỏ qua khi học nhưng là câu hỏi PL-600 điển hình.

---

## 5. `nx_activation` — Audit trail

Bất biến: chỉ thêm, không sửa/xoá. Plug-in `WriteActivationAuditPlugin` (async) ghi.

| Table | Giá trị |
|---|---|
| Display name | `Activation` · Plural `Activations` |
| Schema | `nx_activation` |
| Primary column | `Subject` → `nx_subject` |

| # | Display name | Name | Type | Required | Cấu hình |
|---|---|---|---|---|---|
| 1 | Subject | `nx_subject` | Text (200) | ✅ primary | Plug-in sinh: `"Activate NEX-0001 → nexus"` |
| 2 | License Key | `nx_licensekey` | **Lookup** → `nx_licensekey` | ✅ | |
| 3 | Action | `nx_action` | Choice (**global** `nx_activationaction`) | ✅ | Issue · Activate · Renew · Revoke · Expire |
| 4 | Acted On | `nx_actedon` | Date and Time — **User Local** | ✅ | |
| 5 | Actor | `nx_actor` | Text (200) | ⬜ | Email user thực hiện |
| 6 | From Status | `nx_fromstatus` | Text (20) | ⬜ | Từ `PreEntityImage` |
| 7 | To Status | `nx_tostatus` | Text (20) | ⬜ | Từ `PostEntityImage` |
| 8 | Note | `nx_note` | Multiline text (2000) | ⬜ | |

> 💡 `nx_fromstatus`/`nx_tostatus` để **Text** chứ không phải Choice — audit trail phải giữ nguyên giá trị lịch sử kể cả khi Choice bị sửa về sau. Đây cũng là lý do để dùng `PreEntityImages`/`PostEntityImages` trong plug-in — đúng trọng tâm PL-400.

---

## 6. Relationships + Cascade behavior

Tạo từ table phía **N**: Relationships → **+ New relationship → Many-to-one**.

| # | Table (N) | → Related (1) | Lookup column | Required | **Delete** | Lý do |
|---|---|---|---|---|---|---|
| 1 | `nx_licensekey` | `nx_customer` | `nx_customer` | ✅ | **Restrict** | Không xoá khách khi còn license — mất lịch sử bán hàng |
| 2 | `nx_licensekey` | `nx_licenseplan` | `nx_plan` | ✅ | **Restrict** | Không xoá gói khi còn key tham chiếu |
| 3 | `nx_orgrequest` | `nx_licensekey` | `nx_licensekey` | ✅ | **Restrict** | |
| 4 | `nx_activation` | `nx_licensekey` | `nx_licensekey` | ✅ | **Restrict** | Audit trail không bao giờ nên bị xoá → chặn xoá key khi còn audit |

> 💡 Tương đương `DeleteBehavior.Restrict` của EF Core. Assign/Share/Reparent để mặc định (`NoCascade`).

> ⚠️ **UI Dataverse mới chỉ cho 2 lựa chọn** cho quan hệ tuỳ chỉnh: **Referential** (= `RemoveLink`)
> và **Restrict**. `Cascade` (parental relationship) không còn mở ra vì nó kéo theo cả cascade
> Assign/Share/Reparent, và mỗi bảng chỉ được có đúng một quan hệ parental.
>
> Vì vậy `nx_activation` dùng **Restrict** thay cho Cascade như thiết kế ban đầu:
> - `RemoveLink`: xoá key → audit còn lại nhưng mất tham chiếu, thành bản ghi mồ côi vô nghĩa
> - `Restrict`: chặn xoá key khi còn audit — đúng tinh thần audit trail bất biến
>
> Thực tế license key gần như không bao giờ bị xoá (thu hồi = đổi `Status = Revoked`), nên
> Restrict không gây phiền.

---

## 7. Đối chiếu với DASHBOARD

| Dataverse | → DASHBOARD `Organization` | Ràng buộc DASHBOARD |
|---|---|---|
| `nx_orgrequest.nx_orgname` | `Name` | required, max 200 |
| `nx_orgrequest.nx_orgalias` | `Alias` | required, max 50, **unique global** |
| `nx_orgrequest.nx_orgabout` | `About` | max 2000 |
| `nx_customer.nx_contactemail` | `ContactEmail` | required, max 320 |
| `nx_licensekey.nx_key` | `LicenseKey` | required, max 500, **unique** |
| `nx_licensekey.nx_duedate` | `LicenseDueDate` | required |
| `nx_licensekey.nx_expiredate` | `LicenseExpireDate` | required |
| `nx_licensekey.nx_repocapacity` | `LicenseRepoCapacity` | required — enforce ở `CreateRepositoryCommandHandler.cs:35` |

**Nguyên tắc:** Dataverse là source of truth. DASHBOARD giữ **snapshot copy** tại thời điểm activate — sửa license ở Dataverse **không** tự đổi DASHBOARD, phải qua luồng renew (P7).

### Chưa làm, ghi nhận để sau

| Hạng mục | Vì sao chưa |
|---|---|
| `nx_product` (nhiều sản phẩm) | Hiện chỉ bán DASHBOARD. Thêm khi có sản phẩm thứ 2 |
| Giới hạn số **user** | DASHBOARD chỉ enforce repo capacity (`CreateRepositoryCommandHandler`). Thêm khi backend hỗ trợ |
| Hoá đơn / thanh toán | Ngoài phạm vi — `nx_duedate` chỉ là mốc nhắc |
| Nhiều Org / 1 key | Plan đã chốt **1 key ↔ 1 org** (unique index). Không mở |

---

## 8. Date behavior ⚠️ bất biến

| Column | Behavior | Vì sao |
|---|---|---|
| `nx_licensekey.nx_duedate` | **Date Only** | Ngày lịch — timezone khác nhau không được lệch |
| `nx_licensekey.nx_expiredate` | **Date Only** | Ngày lịch |
| `nx_licensekey.nx_activatedon` | **User Local** | Thời điểm |
| `nx_orgrequest.nx_requestedon` | **User Local** | Thời điểm |
| `nx_orgrequest.nx_completedon` | **User Local** | Thời điểm |
| `nx_activation.nx_actedon` | **User Local** | Thời điểm |

**Quy tắc:** "ngày hiệu lực hợp đồng" → Date Only · "lúc xảy ra sự việc" → User Local.

---

## 9. Tổng kết

| Table | Số column tự tạo | Lookup | Choice |
|---|---|---|---|
| `nx_licenseplan` | 6 | 0 | 0 |
| `nx_customer` | 7 | 0 | 0 |
| `nx_licensekey` | 11 | 2 | 1 |
| `nx_orgrequest` | 13 | 1 | 1 |
| `nx_activation` | 8 | 1 | 1 |
| **Tổng** | **45** | **4** | **3** |

Chưa tính column hệ thống Dataverse tự thêm (`createdon`, `modifiedby`, `ownerid`, `statecode`…)
và column khoá chính (`nx_licenseplanid`, `nx_customerid`, `nx_licensekeyid`, `nx_orgrequestid`,
`nx_activationid`).

> ℹ️ `nx_licensekey` là 11 chứ không phải 12 — cột `Issued On` đã bỏ, xem §10.

### 3 optionset (đều **global**, giá trị bắt đầu từ 0)

| Optionset | Dùng ở | Giá trị |
|---|---|---|
| `nx_draft` *(License Key Status)* | `nx_licensekey.nx_status` | Draft 0 · Issued 1 · Active 2 · Expired 3 · Revoked 4 |
| `nx_orgrequestprovisionstate` | `nx_orgrequest.nx_provisionstate` | Draft 0 · Approved 1 · Provisioning 2 · Completed 3 · Failed 4 |
| `nx_activationaction` | `nx_activation.nx_action` | Issue 0 · Activate 1 · Renew 2 · Revoke 3 · Expire 4 |

> ⚠️ Vì là **global optionset**, giá trị bắt đầu từ **0** chứ không phải `100000000` như local
> optionset. Hằng số trong `LicensingConstants.cs` đã theo giá trị thật này.
>
> 📌 Tên `nx_draft` là do Dataverse tự đặt theo item đầu tiên khi tạo Choice — không đổi được
> sau đó. Hơi khó đọc nhưng vô hại.

---

## 10. Đã bỏ cột `Issued On`

**Vấn đề (2026-09-20):** mọi lần tạo cột `nx_issuedon` với Time zone adjustment = `Date only`
đều ra `Behavior=2` (Time-Zone Independent) và bị khoá ngay lúc tạo — dù UI hiển thị đúng
`Date only`.

Đã thử và **đều thất bại**:

| Lần | Cách làm | Kết quả |
|---|---|---|
| 1 | Tạo `nx_issuedon`, Business required | `Behavior=2` LOCKED |
| 2 | Xoá, tạo lại cùng tên | `Behavior=2` LOCKED |
| 3 | Tạo `nx_issuedate` (schema name khác) | `Behavior=2` LOCKED |
| 4 | Đổi cột hỏng sang Optional | `Behavior=2` LOCKED (Required đổi được, behavior không) |
| 5 | Xoá, tạo lại `nx_issuedon` Optional ngay từ đầu | `Behavior=2` LOCKED |

**Hai giả thuyết đã bị bác bỏ:**
- ~~Metadata dính theo schema name~~ → lần 3 dùng tên hoàn toàn mới, vẫn hỏng
- ~~`Business required` làm hỏng behavior~~ → lần 5 để Optional từ đầu, vẫn hỏng

Tương quan Required/Behavior quan sát được ở lần 3 chỉ là trùng hợp trên mẫu nhỏ.

**Nguyên nhân thật: chưa xác định.** `nx_duedate` và `nx_expiredate` cùng `Format=date`, cùng
giao diện, cùng người tạo → ra `Behavior=3` bình thường (`CanChangeDateTimeBehavior=1`).

### Quyết định: bỏ hẳn cột này

Không mất gì về nghiệp vụ. Ngày phát hành key đã có ở hai chỗ:

| Nguồn | Nội dung |
|---|---|
| `createdon` | Dataverse tự tạo cho mọi bảng |
| `nx_activation` với `Action = Issue` | `nx_actedon` (behavior ✅) + actor + from/to status + note |

Thực ra `nx_activation` là nơi **đúng hơn** để lưu mốc này — nó ghi cả người thực hiện và ngữ
cảnh chuyển trạng thái. Một cột date trên `nx_licensekey` là dư thừa.

> ✅ **Đã dọn (2026-09-21):** cả `nx_issuedon` và `nx_issuedate` đã xoá khỏi `nx_licensekey`.
> Bảng chỉ còn 11 cột tự tạo.

### Dấu hiệu nhận biết cột date bị khoá

Trong `customizations.xml`, cột còn đổi được behavior sẽ có tag `<CanChangeDateTimeBehavior>1</...>`.
Cột đã khoá **không có tag này**. Trên UI: mọi field trong panel Edit column bị xám, nút Save mờ.

`Time-Zone Independent` (Behavior = 2) là trạng thái **một chiều** — vào được, không ra được.

