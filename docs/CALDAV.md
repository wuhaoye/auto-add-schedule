# iCloud / CalDAV 同步

## 协议

iCloud 日历使用标准 CalDAV（RFC 4791）：

1. PROPFIND https://caldav.icloud.com/ 获取 current-user-principal
2. PROPFIND principal 获取 calendar-home-set
3. PROPFIND calendar-home 列出日历（resourcetype 含 calendar），同时读取显示名
4. PUT {calendarUrl}/{uid}.ics 创建事件；DELETE 删除；更新使用 If-Match: {etag}

认证：HTTP Basic，用户名 = Apple ID 邮箱，密码 = 应用专用密码（不是账号密码）。

## 事件格式

按 RFC 5545 生成：

```text
BEGIN:VCALENDAR
VERSION:2.0
PRODID:-//AutoAddSchedule//CN
BEGIN:VEVENT
UID:...
DTSTAMP:20261113T120000Z
DTSTART:20261125T124500Z
DTEND:20261125T130000Z
SUMMARY:...
LOCATION:...
DESCRIPTION:...
BEGIN:VALARM
TRIGGER:-PT10M
ACTION:DISPLAY
DESCRIPTION:...
END:VALARM
END:VEVENT
END:VCALENDAR
```

时间以 UTC 写入（本地时间转换），避免 VTIMEZONE 兼容问题；全天事件使用 VALUE=DATE。

## 错误处理

- 认证失败（401）：提示检查 Apple ID 与应用专用密码
- 网络不可用：日程保存在本地并标记未同步，联网后可手动「刷新同步」
