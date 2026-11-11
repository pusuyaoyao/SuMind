# GitHub 拓展目录与发布约定（待验收）

目录读取地址固定为 `https://raw.githubusercontent.com/pusuyaoyao/SuMind/main/catalog/extensions.json`；目录不存在时主程序应显示暂无目录，不得声称已安装。目录是未经签名的外部数据，不是信任锚。任何下载的拓展包都必须使用应用内可信公钥验证签名后才能安装或执行；SHA-256 只用于完整性检查。

目录为 UTF-8 JSON，`schemaVersion` 固定为 1，`extensions` 为数组；每个架构可对应一条记录，同一 ID 与架构不重复。以下仅为字段示意，不代表该版本已经发布：

```json
{
  "schemaVersion": 1,
  "extensions": [{
    "id": "desktop-navigation",
    "name": "桌面导航",
    "description": "管理任务栏外观",
    "version": "1.0.0",
    "minHostVersion": "1.0.0",
    "architecture": "win-x64",
    "packageUrl": "https://github.com/pusuyaoyao/SuMind/releases/download/desktop-navigation-v1.0.0/DesktopNavigation-1.0.0-win-x64.suminds",
    "sha256": "64位十六进制摘要",
    "size": 12345,
    "signature": "拓展包签名的Base64编码"
  }]
}
```

实际发布时，先构建和测试拓展、创建 `.suminds` 文件；使用 RSA 3072 位或以上的私钥、RSA-PSS/SHA-256 对完整包字节生成分离签名；把包添加到版本化 GitHub Release，记录文件实际长度、SHA-256 和签名的 Base64 编码；最后更新目录。校验用的 SubjectPublicKeyInfo PEM 公钥必须内置于主程序，不能来自目录。不得将签名私钥提交或上传到仓库，密钥轮换方案须在正式发布前确定。

目录和包读取有数量与大小限制。Release 资源 URL 必须为当前仓库的 HTTPS 地址，下载时校验重定向主机、大小和 SHA-256。签名验证、版本兼容和安装事务由安装层负责。

拓展中心也提供本地安装入口。当前格式使用分离签名，本地选择 `.suminds` 文件后仍须联网获取匹配的当前架构和版本目录项（含签名）；然后与在线安装一样验证固定公钥的签名。旧版本及没有目录记录的包不能用此流程安装。当前尚未配置可信发布公钥，也没有正式拓展包，因此这两个安装入口都不能完成正式安装；离线安装需要未来定义包含签名的包格式或其他独立可信元数据。

默认拓展目录为 `%LOCALAPPDATA%\SuMind\extensions`。设置允许在当前目录尚无拓展或残留目录时选择新的本地空目录，不会自动迁移或删除已有文件。选定目录应由用户控制，避免使用其他用户可写入的共享位置。
