# GG Maps

<p align="center">
  <strong>Ứng dụng Google Maps không chính thức cho Windows Phone 8.1, giao diện mô phỏng ứng dụng Google Maps trên iOS.</strong><br>
  <em>Viết bằng C# / XAML (WinRT, wpa81), tối ưu cho Nokia Lumia 1020. Cùng kiến trúc với GGTranslate, GG Drive và YTMusicWP.</em><br>
  <em>Không cần khoá API, không cần đăng nhập, không tốn phí.</em>
</p>

---

## Tải xuống

| Phiên bản | Kiến trúc | Tải về |
|---|---|---|
| **1.0.1.0** | ARM (Lumia 1020 và các máy Windows Phone 8.1 khác) | [GGMaps_1.0.1.0_ARM.appx](https://github.com/Hoangxee/ggMaps/raw/main/releases/GGMaps_1.0.1.0_ARM.appx) |

Gói `.appx` chưa ký, dùng để cài thử (sideload) lên máy đã mở khoá developer (hoặc interop unlock):
1. Cắm điện thoại vào máy tính qua USB và **mở khoá màn hình**.
2. Mở **Windows Phone Application Deployment 8.1** (đi kèm Windows Phone SDK), chọn thiết bị **Device**, chọn tệp `.appx` vừa tải rồi bấm **Deploy**. Hoặc dùng dòng lệnh:
   ```powershell
   & "${env:ProgramFiles(x86)}\Microsoft SDKs\Windows Phone\v8.1\Tools\AppDeploy\AppDeployCmd.exe" /install GGMaps_1.0.1.0_ARM.appx /targetdevice:de
   ```
3. Điện thoại phải có kết nối Internet (Wi-Fi / 4G). Cáp USB chỉ dùng để cài, không chia sẻ mạng.
4. Mở **GG Maps** → tìm địa chỉ nhà / nơi hay xuất phát → mở địa điểm → **Đặt làm mặc định** (xem [Địa chỉ mặc định](#địa-chỉ-mặc-định-thay-cho-gps)).

> AppDeployCmd trên Windows 10 / 11 đôi khi bị crash (`-1073741819`), nhất là khi màn hình điện thoại đang khoá: mở khoá máy rồi chạy lại vài lần. Khi cập nhật bản mới có số phiên bản cao hơn, dùng `/update` thay cho `/install` để giữ dữ liệu (địa chỉ mặc định, lịch sử tìm kiếm).

---

## Tính năng

### Bản đồ
- Bản đồ Google thật (ô ảnh từ máy chủ bản đồ của Google), vẽ bằng bộ hiển thị bản đồ riêng của ứng dụng: kéo có quán tính, phóng to bằng hai ngón, xoay bằng hai ngón, chạm đúp để phóng to.
- **Loại bản đồ**: Mặc định / Vệ tinh / Địa hình, lớp **Giao thông** trực tiếp (nút lớp bản đồ ở góc phải). Bản đồ tối khi chọn giao diện Tối.
- Chấm xanh tại **địa chỉ mặc định**; nút vị trí đưa bản đồ về đó. Nút **la bàn** hiện khi bản đồ bị xoay, chạm để đưa về hướng Bắc.
- Chạm vào bản đồ để ẩn / hiện thanh tìm kiếm và các nút. **Nhấn giữ** để thả ghim: địa chỉ khu vực, toạ độ, nút Đường đi / Đặt làm mặc định / Chia sẻ.

### Tìm kiếm
- Thanh tìm kiếm dạng viên thuốc, hàng **danh mục** (Nhà hàng, Cà phê, Cây xăng, Khách sạn, Mua sắm, ATM, Bệnh viện, Nhà thuốc, Bãi đỗ xe).
- Màn hình tìm kiếm: **tìm kiếm gần đây** (lưu trên máy), **gợi ý tự động** kèm khoảng cách, nút ↖ để điền gợi ý vào ô, **tìm bằng giọng nói**.
- Kết quả **ưu tiên quanh địa chỉ mặc định** (khoảng 10 km) và **xếp từ gần đến xa**; ghim đỏ có tên trên bản đồ + danh sách trong **bottom sheet** kéo được (thu nhỏ / nửa / toàn màn hình) với điểm đánh giá, sao, loại, khoảng cách, giờ mở cửa ("Đang mở cửa · Đóng cửa vào 22:00"), ảnh, nút Đường đi.
- Bộ lọc **Đang mở cửa**, **Xếp hạng 4.0+**, **Gần nhất**; nút **Tìm kiếm khu vực này** để tìm ở chỗ khác trên bản đồ.

### Thông tin địa điểm
- Tên, đánh giá, loại, giờ mở cửa, các nút **Đường đi / Gọi / Trang web / Đặt làm mặc định / Chia sẻ**, dải ảnh (chạm để xem toàn màn hình, vuốt qua lại).
- Thẻ **Tổng quan** (địa chỉ, giờ mở cửa từng ngày, trang web, điện thoại, Plus Code), **Đánh giá**, **Giới thiệu**.

### Chỉ đường
- Điểm xuất phát mặc định là **địa chỉ mặc định**, nút đảo chiều, chọn điểm đi / đến từ màn hình tìm kiếm.
- Phương tiện: **Xe máy** (mở sẵn), **Ô tô**, **Phương tiện công cộng**, **Đi bộ**, **Xe đạp**; thời gian hiện dưới biểu tượng của các phương tiện đã tính.
- Nhiều tuyến cùng lúc trên bản đồ (tuyến chọn màu xanh, tuyến khác màu xám, nhãn thời gian chạm để chọn); ô tô có thời gian theo tình trạng giao thông.
- Danh sách tuyến: thời gian, quãng đường, "Qua …"; xe buýt: giờ đi / đến, tần suất, giá vé, chuỗi tuyến xe.
- Màn hình **Các bước**: biểu tượng rẽ, chỉ dẫn, quãng đường; xe buýt: số tuyến, hướng, điểm lên / xuống kèm giờ, số điểm dừng. Chạm một bước để xem đoạn đó trên bản đồ.
- **Tuỳ chọn tuyến đường**: tránh đường cao tốc, đường thu phí, phà. Chia sẻ tuyến đường (liên kết Google Maps).

### Không làm (theo phạm vi dự án)
Street View, dẫn đường từng bước có giọng đọc, Đã lưu, Danh sách, Dòng thời gian, Đóng góp, Viết đánh giá, Bản đồ ngoại tuyến, Đăng nhập tài khoản Google, định vị GPS.

---

## Địa chỉ mặc định (thay cho GPS)

Ứng dụng **không dùng GPS** (trên Windows Phone 8.1 hiện nay, định vị qua Wi-Fi / trạm di động không còn hoạt động và GPS trong nhà gần như không bắt được). Thay vào đó bạn đặt một **địa chỉ mặc định**, dùng làm:
- điểm xuất phát khi chỉ đường (ô xuất phát hiện tên địa chỉ này),
- vị trí chấm xanh trên bản đồ,
- tâm tìm kiếm: kết quả gần địa chỉ này nhất được xếp lên đầu.

Cách đặt:
- Mở một địa điểm (hoặc nhấn giữ trên bản đồ để thả ghim) → **Đặt làm mặc định**, hoặc
- **Cài đặt → Địa chỉ mặc định → Chọn địa chỉ…** rồi tìm và chọn. Nút **Xoá** để bỏ.

---

## Nguồn dữ liệu (không cần khoá API, không tốn phí)

Giống GGTranslate, ứng dụng gọi thẳng các endpoint mà Google Maps bản web dùng:

| Chức năng | Endpoint |
|---|---|
| Ô bản đồ (mặc định / vệ tinh / địa hình / giao thông, bản đồ tối bằng `apistyle`) | `mt0-3.google.com/vt` |
| Gợi ý tự động | `www.google.com/s?tbm=map&suggest=p` |
| Tìm kiếm, chi tiết địa điểm (đánh giá, ảnh, giờ mở cửa, mô tả) | `www.google.com/search?tbm=map` |
| Địa chỉ của ghim thả tay | `www.google.com/maps/preview/reveal` |
| Chỉ đường ô tô / đi bộ / xe buýt | `www.google.com/maps/preview/directions` |
| Chỉ đường **xe máy** và **xe đạp** | [BRouter](https://brouter.de) (dữ liệu OpenStreetMap, hồ sơ `moped` / `trekking`) |

Google Maps bản web không có chế độ xe máy / xe đạp ở Việt Nam (chỉ app Android / iOS có), nên hai chế độ này dùng BRouter: tuyến dành cho xe máy (không đi cao tốc, đi được đường nhỏ). Dữ liệu BRouter không có tên đường nên chỉ dẫn có dạng "Rẽ trái rồi đi tiếp 1,4 km", "Tại vòng xuyến, đi theo lối ra thứ 2"; thời gian tính theo tốc độ xe máy nhỏ, không có tình trạng giao thông. (Máy chủ Valhalla của OpenStreetMap chỉ chấp nhận TLS 1.3, Windows Phone 8.1 không kết nối được.)

**Rủi ro:** đây là endpoint không chính thức. Tham số `pb` là định dạng nội bộ của Google, có thể đổi bất cứ lúc nào và làm hỏng tìm kiếm / chỉ đường cho tới khi cập nhật ứng dụng; dùng quá dày Google có thể tạm chặn mạng (`google.com/sorry`). BRouter là dịch vụ cộng đồng, chỉ nên dùng ở mức cá nhân.

---

## Build

Yêu cầu: **MSBuild 14.0** (Visual Studio 2015 Build Tools) và **Windows Phone 8.1 SDK**.

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1                       # Release | ARM -> GGMaps\AppPackages\GGMaps_<ver>_ARM_Test\
powershell -ExecutionPolicy Bypass -File build.ps1 -Rebuild -Deploy      # build lại + cài lên máy đang cắm USB
```

- `packages\` (Newtonsoft.Json 13.0.4 và 3 gói System.*) không được commit; `build.ps1` tự sao chép từ `D:\lumia1020\YTMusicWP\packages` (hoặc khôi phục bằng NuGet theo `GGMaps\packages.config`).
- Phiên bản nằm ở `Identity Version` trong `GGMaps\Package.appxmanifest` (tăng mỗi bản để `/update` được).
- Ảnh tile / splash: `powershell -ExecutionPolicy Bypass -File tools\make-assets.ps1`.
- Nhật ký chẩn đoán: `LocalFolder\diag.log` (đọc bằng `ISETool ts appxfolder:Local de 87513662-4747-445a-871b-68bfde20e521 <thư mục>`).

---

## Cấu trúc mã nguồn

```text
GGMaps.sln, build.ps1, tools\make-assets.ps1, releases\ (gói .appx đã build)
GGMaps\
  App.xaml(.cs)            bảng màu sáng/tối (Google Maps), font, chủ đề
  MainPage.xaml(.cs)       toàn bộ UI (bản đồ + bottom sheet + các lớp phủ), trạng thái dùng chung, toast, bàn phím, chia sẻ
  Controls\GMap.cs         bộ hiển thị bản đồ (Web Mercator, ô 256 px, kéo / phóng / xoay, ghim, đường, đa giác);
                           thay cho MapControl của Windows Phone vốn không còn hiển thị được
  Pages\MainPage.*.cs      Map (ô bản đồ, camera, ghim, vẽ tuyến), Location (địa chỉ mặc định, chấm xanh, la bàn),
                           Sheet (bottom sheet kéo thả), Navigation (màn hình, nút Back, danh mục), Search (gợi ý, lịch sử,
                           giọng nói), Results (kết quả, bộ lọc, tìm khu vực này), Place (thẻ địa điểm, ảnh, ghim thả tay),
                           Directions (phương tiện, tuyến, các bước, tuỳ chọn), Layers, Settings
  Services\                MapsApi (HTTP, chặn / offline, đọc mảng JSON của Google), TileService (ô bản đồ mt0-3),
                           PlacesService (gợi ý, tìm kiếm, chi tiết, reveal), RoutesService (directions của Google +
                           BRouter cho xe máy / xe đạp), PolylineCodec, HistoryStore, AppSettings, Fmt, Icons, DiagLog
  Models\                  LatLng / Waypoint, Place, Suggestion, RouteInfo (+ RouteStep, TransitInfo, RouteRow, StepItem), HistoryItem
  Converters\              BoolToVisibilityConverter, PathDataConverter
```

---

## Hạn chế đã biết
- Bản đồ là ảnh raster: khi xoay bản đồ, chữ trên bản đồ xoay theo; không có chế độ 3D / nghiêng.
- Không thể chạm vào địa điểm có sẵn trên bản đồ (bản đồ là ảnh); hãy tìm kiếm hoặc nhấn giữ để thả ghim.
- Tìm kiếm bằng giọng nói dùng ngôn ngữ giọng nói của máy (Windows Phone 8.1 không có tiếng Việt).
- Khi máy không có mạng, Windows Phone 8.1 trả lỗi 404 giả cho mọi yêu cầu; ứng dụng báo "Không có kết nối Internet" và tự tải lại bản đồ khi có mạng.

---

## Giấy phép và ghi công
- Bản đồ, địa điểm, ảnh, đánh giá và tuyến đường ô tô / đi bộ / xe buýt: **Google Maps**. Tuyến xe máy / xe đạp: **BRouter**, dữ liệu © OpenStreetMap contributors (ODbL).
- **Google Sans Flex**: SIL Open Font License 1.1 (`GGMaps/Assets/Fonts/OFL.txt`).
- **Material Symbols / Material Icons**: Google, Apache License 2.0 (dữ liệu path của biểu tượng).
- "Google" và "Google Maps" là nhãn hiệu của Google LLC. Dự án này không liên kết với Google.
