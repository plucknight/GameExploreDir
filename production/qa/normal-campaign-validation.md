# 普通设计与原型验证记录

日期：2026-10-23。范围：普通设计、数据、机制原型与本机Windows验证。不是发行验收。

| 层级 | 当前状态 | 边界 |
|---|---|---|
| 普通关卡/市场静态结构 | PASS | 六区54关162遭遇、36怪物模板、24市场模板 |
| 公式与概率计算 | PASS | 基础攻击、预算求和、带保底稀有期望；不等于真实经济平衡 |
| Python坏数据与跨文件测试 | PASS，31项 | 源数据、错误边界、跨文件规则、链接与衍生资源 |
| Unity工程创建 | PASS | 真实6000.6.5f1创建，退出0 |
| Unity导入/编译/领域检查 | PASS，10组 | 9组领域检查＋1组时钟检查；另列未验证事项 |
| Windows Player构建 | PASS | StandaloneWindows64，Editor退出0 |
| Windows Player运行与画面 | PASS，有限范围 | 可见窗口1440×1000、1280×720实际启动、截图、退出0；默认Direct3D 12 |
| 真人鼠标交互与三窗理解 | NOT ASSESSED | 自动截图与源码检查不能代替操作观察 |
| 陌生玩家/50小时体验 | NOT RUN | 没有人工试玩样本 |
| 完整经济/配方闭环 | NOT VALIDATED | 尚缺实际成本、产率与战斗数据 |
| Steam生产接入 | NOT IMPLEMENTED | 无真实发奖、库存消耗或市场发布 |

本机完整日志位于忽略目录 `production/qa/evidence/local/`，最终摘要保留在本文件；不将私有环境日志、凭据或构建缓存提交仓库。

## 可复现命令与证据

```powershell
py -3 tools/prepare_prototype.py --check
py -3 tools/check_design.py
py -3 -m unittest discover -s tests -v
powershell -ExecutionPolicy Bypass -File tools/build_prototype.ps1 -EditorPath 'C:\Program Files\Unity\Hub\Editor\6000.6.5f1\Editor\Unity.exe'
```

图形回归：运行构建出的`NormalCampaign.exe -prototype-smoke -screen-width 1440 -screen-height 1000 -screen-fullscreen 0`；另测1280×720。需要可见窗口；隐藏运行返回捕获失败或黑图，不计为通过。截图来自真实Player帧，没有用设计图替代。

- [静态与数学摘要](evidence/design-checks.json)
- [Unity领域与时钟检查结果](evidence/unity-domain-checks.json)
- [1440×1000实际三队画面](evidence/three-teams-1440x1000.png)
- [1280×720实际三队画面](evidence/three-teams-1280x720.png)
- [原型体验报告](../../prototypes/NormalCampaign/REPORT.md)

三队窗口在两种分辨率下同时可见，中文、配置和状态能渲染；720p需要滚动查看下方配置/招募，视口通知固定可见。这个结论不包含陌生玩家是否看得懂或会不会操作。暂未做长时间后台耗电、内存或设备兼容测试。

## 规则检查覆盖

1. 54关顺序、每关三场、召唤引用与市场产地。
2. 初始两人一队、招募扣料、技能/基础纹章及第二槽门槛。
3. 三队占用与成员边界、账号时间不乘三、完整趟资格切换。
4. 普通房间奖励保留、败退时间、重试满血清状态。
5. 三败安全回退，改装后安全记录失效。
6. Stop/Start、换队和卸下无法跳过胜败休整；当前失败关不能作为回退目标。
7. 仅目标队整趟成功提交资格，失败不提交；稀有20次保底计数。
8. 3-9实际自动战斗观察到有限召唤，活敌最多三名。
9. 固定种子600秒与分块推进结果一致。
10. 低帧积压不丢时间；暂停和长时间无帧不奖励模拟时间。

部分边界使用故障注入，明确只验证状态机。第9项快速原型在600秒内通过17关，不能拿来验证55—65小时；仍缺完整经济成本、产率、普通装备与全部纹章效果的联调。

## 保留的风险

生产保存/恢复、离线基础收益和真实市场事务未实现；关闭原型会丢弃本次会话，与最终游戏永久成长承诺不同。首次使用绑定和区域装备许可已有设计及静态约束，尚未在真实Steam资产中验证。多数挂机玩家的喜好、无专职治疗替代路线、最终Boss配置覆盖和第10/20/35/50小时回访仍需真实试玩。
