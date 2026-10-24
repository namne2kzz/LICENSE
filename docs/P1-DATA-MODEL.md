# P1 — Solution + Data model

> ✅ **Hoàn tất 2026-09-21.** Ghi chép cách đã làm trên https://make.powerapps.com.
> Đặc tả bảng/cột: [DATA-MODEL.md](DATA-MODEL.md) — nguồn sự thật.

## Kết quả

| Mục | Giá trị |
|---|---|
| Publisher | `Nexus` prefix `nx` |
| Solution | `NexusLicensing` v1.0.0.0 |
| Bảng | 5 · 45 column tự tạo · 3 optionset global |
| Relationship | 4, **tất cả `Restrict`** |
| Alternate key | `nx_UniqueLicenseKey` trên `nx_key` — Active |
| Model-driven app | `NKey`, 5 page |
| Dữ liệu | 3 License Plan: TRIAL (3/3th) · STD (10/12th/grace 15) · ENT (50/12th/grace 30) |
| Trong git | `solution/NexusLicensing/` — 90 file |

```powershell
.\scripts\verify-schema.ps1        # → "=== P1 HOAN TAT ==="
.\scripts\verify-licenseplan.ps1   # → "TAT CA DUNG SPEC" (8/8)
```

---

## Vì sao làm tay trên portal

`pac` CLI **không tạo được** publisher, solution, table, column mới trên server — chỉ
import/export solution, query đọc (`env fetch`), và sinh code (`modelbuilder`). Những việc đó phải
làm trên Maker portal.

CLI bắt buộc từ **P5** (`pac pcf init`) và **P6** (export solution vào git).

---

## Thứ tự đã làm

Lookup cần table đích tồn tại trước:

| # | Việc | Tham chiếu |
|---|---|---|
| 1 | Publisher `Nexus` / prefix `nx` | — |
| 2 | Solution `NexusLicensing` | — |
| 3 | `nx_licenseplan` | DATA-MODEL.md §1 |
| 4 | `nx_customer` | §2 |
| 5 | `nx_licensekey` (lookup → customer, plan) | §3 |
| 6 | `nx_orgrequest` (lookup → licensekey) | §4 |
| 7 | `nx_activation` (lookup → licensekey) | §5 |
| 8 | Alternate key `nx_UniqueLicenseKey` | §3 |
| 9 | Delete behavior 4 relationship → `Restrict` | §6 |
| 10 | Model-driven app + nhập 3 License Plan | — |

### Thao tác

**Publisher** — Solutions → New solution → link **New publisher**. Prefix quyết định tên mọi
table/column (`nx_*`), **không đổi được** sau khi đã tạo table.

**Solution** — Solutions → **+ New solution**, chọn publisher `Nexus (nx)`.

**Table** — trong solution → **Objects** → **+ New → Table → Table (blank)** → mở
**Advanced options** đặt Primary column theo spec.

**Column** — mở table → **+ New → Column**. Required level và Min/Max nằm trong **Advanced options**.

**Lookup** — ở table phía **N**, tạo column kiểu **Lookup** rồi chọn Related table. Dataverse tự
sinh relationship; vào **Relationships** chỉnh delete behavior sau.

**Choice** — Data type *Choice* → **New choice** → thêm item. Để Dataverse tự sinh value.

**Alternate key** — mở `nx_licensekey` → tab **Keys** → **+ New key** → chọn `nx_key` → đợi status
**Active**.

**Model-driven app** — Solutions → **Objects** → **+ New → App → Model-driven app** → Add page →
chọn 5 bảng → Save → Publish.

> ⚠️ Luôn tạo **bên trong solution**. Tạo từ menu *Tables* ngoài solution → component không gắn vào
> solution → P6 export sẽ thiếu. Trang **Overview** của solution không có nút `+ New` — phải vào
> **Objects**.

---

## 🔴 Ba thứ không sửa được sau khi tạo

| Thứ | Hậu quả nếu sai |
|---|---|
| **Date behavior** | Phải xoá cột tạo lại — và `Time-Zone Independent` thì **không cứu được** kể cả tạo lại |
| **Primary column** (type, schema name) | Phải xoá cả table |
| **Publisher prefix** | Phải tạo lại toàn bộ table |

Sửa được thoải mái: thêm column · Display name · Required ↔ Optional · item trong Choice ·
Min/Max · MaxLength (chỉ nới rộng) · delete behavior của relationship.

Xoá column được, nhưng phải gỡ hết phụ thuộc trước (form, view, business rule, flow, plug-in step,
alternate key) — xem **Dependencies** của column.

---

## 🔑 Date behavior nằm ở dropdown "Time zone adjustment"

Solution editor có **hai** dropdown dễ nhầm khi tạo column Date and time:

| Dropdown | Lựa chọn | Quyết định gì |
|---|---|---|
| **Behavior** | Simple · Calculated · Rollup | **Loại column** — để `Simple`. KHÔNG liên quan múi giờ |
| **Time zone adjustment** | User local · Time-Zone Independent · **Date only** | ⭐ **Đây mới là date behavior** |

Ánh xạ sang `customizations.xml`:

| Time zone adjustment | `<Behavior>` | Dùng cho |
|---|---|---|
| User local | 1 | `nx_activatedon`, `nx_requestedon`, `nx_completedon`, `nx_actedon` |
| Time-Zone Independent | 2 | ❌ **không bao giờ chọn** — xem DATA-MODEL §10 |
| **Date only** | **3** | `nx_duedate`, `nx_expiredate` |

> ⚠️ "Time-Zone Independent" nghe như "không phụ thuộc múi giờ" nên rất dễ chọn nhầm, nhưng nó
> **vẫn lưu phần giờ** và là **ngõ cụt một chiều** — cột dính rồi thì xoá tạo lại cũng không thoát,
> phải bỏ hẳn cột đó. Đã mất cột `Issued On` vì lỗi này.

---

## ⚠️ KHÔNG dùng canvas editor "Create new tables"

Power Apps có 2 giao diện tạo table:

| | Canvas "Create new tables" | Solution editor ✅ |
|---|---|---|
| Nhận biết | Lưới chấm, kéo thả nhiều table cùng lúc | Panel form dọc bên phải |
| Vào từ | Trang Tables → Create new tables | **Solutions → Objects → + New → Table** |
| Min/Max số | ❌ | ✅ |
| MaxLength text | ❌ (mặc định 100, primary 850) | ✅ |
| **Date behavior** | ❌ | ✅ |
| Lưu | Chỉ lưu nháp client, phải bấm nút lưu cuối của canvas | Save ghi thẳng lên Dataverse |

**Đã gặp:** `nx_licenseplan` tạo bằng canvas → tên cột và Required level đúng, nhưng:

| Column | Canvas tạo ra | Đã sửa thành |
|---|---|---|
| `nx_planname` | MaxLength 850 | 100 |
| `nx_plancode` | MaxLength 100 | 20 |
| `nx_repocapacity` | Min −2147483648 | Min **1** · Max **1000** |
| `nx_durationmonths` | Min −2147483648 | Min **1** · Max **120** |
| `nx_gracedays` | Min −2147483648 | Min **0** · Max **90** |

`Min = −2147483648` nghĩa là nhập `repoCapacity = -5` vẫn lưu được — giá trị đó sẽ chảy sang
`Organization.LicenseRepoCapacity` của DASHBOARD. Đã sửa bằng solution editor.

> Min/Max/MaxLength sửa được sau nên không mất gì. Nhưng canvas cũng không cho đặt **date
> behavior** — nếu `nx_licensekey` tạo bằng canvas thì 3 cột date đã hỏng vĩnh viễn. Đó là lý do
> 4 bảng còn lại đều làm bằng solution editor.

---

## Vướng đã gặp

| Triệu chứng | Nguyên nhân | Xử lý |
|---|---|---|
| Table ra tên `cr123_...` thay vì `nx_...` | Tạo ngoài solution, hoặc solution dùng publisher mặc định | Xoá, tạo lại **bên trong** solution |
| Display name bảng kẹt ở `Reserve entity 29722115c2` | Xoá rồi tạo lại bảng cùng tên quá nhanh | Schema name vẫn đúng → sửa Display name ở **Properties** |
| Lookup trùng (2 cột cùng trỏ 1 bảng, đều required) | Tạo lookup hai lần | Xoá một cái |
| Không đổi được Date behavior | **Bất biến** | Xem mục Date behavior ở trên |
| Alternate key kẹt *Pending* | Dữ liệu có giá trị trùng/rỗng | Dọn dữ liệu → xoá key → tạo lại |
| Không thấy table trong app designer | Chưa publish | **Publish all customizations** |
| Không kéo thả được group trong App Designer | Hạn chế của designer mới | Bỏ qua — chỉ là thẩm mỹ |

### Whole number không có ô Default value

Solution editor mới bỏ ô Default cho Whole number. `nx_gracedays` vì vậy không có default `0`.

**Không cần thiết:** cột Optional, null và 0 cho cùng kết quả khi plug-in đọc
`GetAttributeValue<int?>(...) ?? 0`. Thêm business rule chỉ để điền số 0 là thừa một component
phải maintain và export.

---

## → P2

[P2-PLUGINS.md](P2-PLUGINS.md) — plug-in C#, đăng ký step/image
