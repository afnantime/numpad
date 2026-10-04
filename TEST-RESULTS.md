# ผลการทดสอบ — 5 ตุลาคม 2026

ระบบที่ใช้: Windows 10 Pro 22H2, build **19045.6466**, .NET Framework **4.8**
(registry Release 533325)

- Build ด้วย .NET Framework C# compiler: ผ่าน
- Automated rules / settings JSON / keyboard state / worker queue tests: **71 assertions ผ่าน**
- เปิด tray และติดตั้ง/ถอด global keyboard hook (`--smoke`): exit code **0**
- Windows Virtual Desktop integration: สลับไป Desktop **1, 5, 9** จริงสำเร็จ
- หลัง integration: กลับ Desktop เดิมและคืนจำนวน Desktop เดิม (**5**)
- integration รวม automated tests: **76 assertions ผ่าน**

การเรียก COM จาก sandbox ถูก Windows ปฏิเสธด้วย E_ACCESSDENIED
จึงทดสอบ integration นอก sandbox สำเร็จ การทดสอบนี้ไม่ได้ติดตั้งโปรแกรม
ไม่ได้เปิดพร้อม Windows และไม่ได้บันทึกการผูกปุ่มให้ผู้ใช้

ยังไม่ได้ตรวจด้วยคีย์บอร์ดจริง: ปุ่มวงเล็บเฉพาะรุ่น, Num Lock จริง,
การกดค้าง/กดเร็วจริง, การพิมพ์ในแอปอื่น และการเปิดพร้อม Windows หลัง sign in
กฎ Num Lock, repeat, key-up หลังเปลี่ยนโหมด, แถวบน และปุ่ม navigation
ครอบคลุมด้วย automated tests แต่ไม่แทนการตรวจคีย์บอร์ดจริง

## ขั้นตอนตรวจด้วยมือ

1. เปิด EXE และตรวจจับปุ่มครบ 4 ช่อง ตรวจว่าปุ่มซ้ำถูกปฏิเสธและเลือก / กับ * แทนได้
2. กดปุ่มเข้าโหมด 1 แล้วทดสอบ Numpad 1–9 ทั้ง Num Lock เปิดและปิด
3. ในโหมด 1 ตรวจเลขแถวบน วงเล็บแถวบน Numpad 0 จุด และปุ่มคำนวณ
4. กด Numpad ค้างแล้วเปลี่ยนโหมด ตรวจว่าไม่สั่งซ้ำและเลขไม่รั่วระหว่างค้าง
5. เข้าโหมด 2 ตรวจว่าพิมพ์เลข/นำทางตาม Num Lock และสถานะ Num Lock ไม่เปลี่ยน
6. ลบ Desktop ผ่าน Task View แล้วกดหมายเลขที่ขาด ตรวจว่าถูกสร้างใหม่
7. เปิดโปรแกรมซ้ำ ตรวจว่ามีเพียง instance เดียว ออกแล้วปุ่มทำงานปกติ
8. เลือกเปิดพร้อม Windows แล้ว sign out/in ตรวจว่าเริ่มโหมด 2

การทดสอบ integration สร้างและลบ Desktop ชั่วคราวเท่านั้น หากจะรันทดสอบอีก
ให้งดจัดการ Desktop หรือย้ายหน้าต่างระหว่างทดสอบ
