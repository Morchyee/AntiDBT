# antiDBT Phase 5.2：GitHub 正式发布报告

## 一、发布概览

- **GitHub 仓库**：[Morchyee/AntiDBT](https://github.com/Morchyee/AntiDBT)
- **目标分支**：`main`（已推送全部有效历史及最新源码）
- **发布版本**：`v0.5.2-beta`（Pre-release）
- **Release 页面**：[antiDBT v0.5.2 Beta Release](https://github.com/Morchyee/AntiDBT/releases/tag/v0.5.2-beta)
- **直接下载链接**：[antiDBT-phase5.2-win-x64.zip](https://github.com/Morchyee/AntiDBT/releases/download/v0.5.2-beta/antiDBT-phase5.2-win-x64.zip)

---

## 二、源码发布与仓库安全核验

1. **远程仓库配置**：
   - `origin` 配置为 `https://github.com/Morchyee/AntiDBT.git`。
   - `main` 分支完整推送，包含 Phase 2、Phase 3、Phase 4、Phase 5 的全套有效 Git 提交记录。
2. **敏感信息与凭据扫描**：
   - 扫描了 Git 提交历史、所有源码、文档与测试用例，确认无任何明文真实 API Key、Token、密码或私有凭据。
   - 密钥与凭据在本地运行环境中统一受 Windows DPAPI 加密保护，测试用例均采用假名 mock/synthetic 结构。
3. **忽略规则（.gitignore）规范**：
   - 严禁提交并成功忽略了：`**/bin/`、`**/obj/`、`publish/`、`artifacts/`、`.packages/`、`.dotnet/`、`.dotnet-home/`、`data/`、`*.log`、`.agents/`、`.vs/`、`.vscode/` 等。
4. **第三方二进制与商业版权检查**：
   - 确认未向仓库提交任何非开源商业软件提取物、反编译产物或私有二进制 DLL。
   - 随包依赖及许可文件包含完整的 MIT 协议与第三方开源许可声明。

---

## 三、发布资产与哈希校验

| 项目 | 校验数据 | 状态 |
| :--- | :--- | :--- |
| **Release Tag** | `v0.5.2-beta` | 已发布 |
| **Release Title** | `antiDBT v0.5.2 Beta` | Pre-release |
| **ZIP 文件名** | `antiDBT-phase5.2-win-x64.zip` | 资产就绪 |
| **ZIP 文件体积** | 63,618,031 字节 (~60.67 MB) | 完整上传 |
| **ZIP SHA256** | `99564B76A607EAC5874455E5D9541758E2E46117A07E25A676444DA30E9602F6` | **完全一致 (Match)** |
| **主程序 `antiDBT.exe` SHA256** | `9BEC28186CEA33BA1E1D394F87305374ABB8E0E5EBB8CB399A8AF86BD8E1C25E` | **完全一致 (Match)** |
| **本地解压验证** | 成功解压并校验 `antiDBT.exe` 完整性 | **PASS** |

---

## 四、README 与文档完善

已将根目录 `README.md` 全面重构为高质量的开源规范文档：
- **项目定位**：清晰阐明为 Windows 10/11 独立实时双语字幕悬浮窗，不依赖特定厂商私有接口。
- **功能矩阵**：全面覆盖多语言选择、系统音频回环采集、10+ ASR 及 14+ MT Provider 配置、0%~100% 背景半透明度调节、窗口自适应、像素级平滑滚动等。
- **免责与独立开发声明**：明确声明本项目为独立纯净室复刻与技术研究项目，非字节跳动/豆包官方产品，与上述实体无任何隶属关系。
- **构建与测试指引**：提供了完整的 .NET 9 SDK 构建命令与自动化测试脚本调用方法。
