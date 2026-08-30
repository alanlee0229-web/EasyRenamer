# Discussions Setup

```text
DISCUSSIONS_CONFIGURATION = USER_ACTION_REQUIRED
```

GitHub API 检查显示仓库当前 `has_discussions=false`。仓库所有者需要在 GitHub 页面完成以下设置：

1. 打开 **Settings → General → Features**。
2. 勾选 **Discussions**。
3. 打开 Discussions 的 Categories 管理。
4. 建立或调整以下类别：

| Category | 建议格式 | 用途 |
| --- | --- | --- |
| Announcements | Announcement | Release、重要状态和维护通知；仅维护者发布 |
| Ideas | Open-ended discussion | 产品想法与 Roadmap 反馈 |
| Q&A | Question / Answer | 使用问题与可接受答案 |
| Show and Tell | Open-ended discussion | 中性、无隐私内容的工作流分享 |
| Development | Open-ended discussion | 架构、贡献和开发讨论 |

类别描述应链接到 [Support](../SUPPORT.md) 和 [Security Policy](../SECURITY.md)。所有类别都应提醒用户不要上传私人文件、敏感文件名、个人路径或未脱敏日志。

完成后可删除本文的 `USER_ACTION_REQUIRED` 状态，并验证 README 与 Issue Template contact link 能正常打开 Discussions。
