# TopMediaBar

แถบสื่อสไตล์ notch ของ macOS สำหรับ Windows ตัวแถบจะซ่อนอยู่ที่ขอบบนสุดของหน้าจอหลัก
แล้วเลื่อนลงมาเมื่อเอาเมาส์เข้าใกล้ขอบบน แสดงเพลง/วิดีโอที่กำลังเล่น (ชื่อเพลง ศิลปิน
ปก) ปุ่มควบคุมการเล่น แถบเลื่อนตำแหน่งเพลง และระดับเสียงเฉพาะแอปนั้น ๆ — จากนั้นจะ
เลื่อนกลับขึ้นไปซ่อนเมื่อเมาส์ออก พร้อมมี toast แจ้งเมื่อเปลี่ยนเพลง

## ดาวน์โหลดแล้วรันได้เลย (ไม่ต้อง compile)

โหลดไฟล์ [`TopMediaBar-win-x64.zip`](TopMediaBar-win-x64.zip) แตกไฟล์ แล้วรัน
`TopMediaBar.exe` ได้ทันที — เป็น self-contained build ไม่ต้องติดตั้ง .NET เพิ่ม
(ต้องใช้ Windows 10 1903+ หรือ Windows 11)

## หลักการทำงาน

- **Now playing / ปุ่มควบคุม** — อ่านและควบคุมการเล่นผ่าน Windows System Media
  Transport Controls (SMTC) จึงตามแอปที่กำลังครองสิทธิ์ session สื่อของระบบอยู่
  (Spotify, เบราว์เซอร์ ฯลฯ)
- **ระดับเสียง** — ควบคุมระดับเสียงของ session แอปที่เป็นเจ้าของสื่อที่กำลังเล่นอยู่
  (ผ่าน Core Audio API ของ NAudio) ไม่ใช่ระดับเสียงหลักของระบบ
- **แสดง/ซ่อนแถบ** — ตรวจตำแหน่งเคอร์เซอร์จริงบนจอ (ไม่ใช้ hit-testing ของ WPF)
  เพื่อลดอาการกระพริบ/เด้งใกล้ขอบจอ แล้วเลื่อนแถบลงมาด้วยอนิเมชันสั้น ๆ
- **Toast** — โผล่ toast เล็ก ๆ แสดงปกและชื่อเพลง/ศิลปินเมื่อเปลี่ยนเพลง ถ้าแถบหลัก
  ยังไม่ได้กางอยู่

## ความต้องการของระบบ (สำหรับ build เอง)

- Windows 10 (1903+) / Windows 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

## รันจากซอร์สโค้ด

```powershell
dotnet run --project TopMediaBar.csproj
```

เมื่อเปิดแอปจะมี tray icon ขึ้นมา คลิกขวาที่ icon เพื่อออกจากโปรแกรม

## Build ไฟล์แจกจ่าย

```powershell
dotnet publish TopMediaBar.csproj -c Release
```

จะได้ `TopMediaBar.exe` แบบ self-contained (ไม่ต้องติดตั้ง .NET runtime แยก)
พร้อม DLL ที่จำเป็นอยู่ใน `bin/Release/net8.0-windows*/win-x64/publish/`
ตั้งใจปิด `PublishSingleFile` ไว้ — ดูเหตุผลในคอมเมนต์ของ `TopMediaBar.csproj`

## การทดสอบ

ชุด regression test อยู่ใน [`tests/`](tests/README.md):

```powershell
dotnet run --project tests/TopMediaBar.RegressionTests.csproj -c Release
```

ดู `tests/README.md` สำหรับ flag เสริม `--audio`, `--media`, `--artwork`

## โครงสร้างโปรเจกต์

| ไฟล์ | หน้าที่ |
|---|---|
| `MainWindow.xaml(.cs)` | UI แถบ notch, อนิเมชันแสดง/ซ่อน, จัดการ input |
| `MediaSessionService.cs` | เชื่อมต่อ SMTC (now playing, ปุ่มควบคุม, seek) |
| `VolumeService.cs` | ควบคุมระดับเสียง/mute เฉพาะแอปผ่าน NAudio |
| `ToastWindow.xaml(.cs)` | popup toast เมื่อเปลี่ยนเพลง |
| `PlaybackTimeline.cs` | ประมาณตำแหน่งการเล่นปัจจุบันระหว่างรอ SMTC อัปเดต |
| `NativeMethods.cs` | Win32 interop (ตำแหน่งเคอร์เซอร์, สไตล์ tool window) |
