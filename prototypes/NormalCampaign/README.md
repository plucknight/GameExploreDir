<!-- PROTOTYPE - NOT FOR PRODUCTION -->
<!-- Question: 玩家能否通过双人配置与战报解决可见威胁？ -->
<!-- Date: 2026-10-23 -->
# 普通地下城 Unity 占位原型

Editor 固定 **6000.6.5f1**，来自本机实际已安装版本；工程由真实 Editor 创建，不安装或升级用户全局工具。采用内置渲染与IMGUI，逻辑与显示分离。一个窗口同时显示三队，不是三个原生窗口。

## 运行

从仓库根目录执行 `py -3 tools/prepare_prototype.py`，然后用指定Editor打开本目录，在 `Assets/Scenes/CampaignPrototype.unity` 场景进入Play。首次场景由下面的构建入口创建。

```powershell
powershell -ExecutionPolicy Bypass -File tools/build_prototype.ps1 -EditorPath 'C:\Program Files\Unity\Hub\Editor\6000.6.5f1\Editor\Unity.exe'
```

生成的本地Player为 `Builds/Windows/NormalCampaign.exe`。构建产物和本机完整日志不提交Git；编译、领域检查、Player观察结果见[验证记录](../../production/qa/normal-campaign-validation.md)。

## 操作

1. 新开局只有两名角色。选关后点击第一队的「派往」，默认自动推进，也可以选择重复刷取。
2. 观察双方小人、血量、护盾、蓄力和最近事件。失败后查看回访摘要。
3. 先停队，等待现有休整结束，再换角色、主动技能、条件、纹章或提升兵装；重新派遣比较结果。换队需要先显式卸下，不会自动抢走另一队成员。纹章悬停显示简化效果。
4. 通过资格关并积累材料后招募；4人+1-5开第二队，6人+1-9开第三队。同一角色和关卡不能多队重复占用。
5. 1/10/60倍是测试加速。实验按钮创建独立的3-9单槽、4-1双槽、5-5减速状态，用于比较配置，不计为自然推进证据。
6. 三窗可独立折叠；折叠不暂停模型。稀有目标切换需先完成或放弃旧趟；只有整趟成功才累计资格。
7. 可导出文本观察摘要；**不是持久存档**。本原型为会话实验，关闭或新开局重置不保留成长。
8. 小屏通过滚动查看下方配置与招募，通知固定在视口底部。高倍速低帧率时，先前积压会继续处理；积压清空前暂禁改队和派遣，可降至1倍继续。超过30秒无帧间隔会暂停，不发离线收益。

## 数据来源与限制

`Assets/Resources/campaign.json` 来自仓库根 `data/normal-campaign.json`，仅将三组敌人数组转换为JsonUtility支持的对象数组，附源SHA-256；`heroes.json`来自角色表。运行 `py -3 tools/prepare_prototype.py --check` 可检测漂移。修改权威数据后重新生成，不能手改衍生数据。

原型验证战斗、配置、顺序解锁和三队观察。基础技能效果、兵装提升与纹章表现是简化候选参数，独立四栏库存、全部演化/被动、生产存档、离线收益、Steam后端事务与真实玩家交易尚未实现。市场只显示本地模拟计数，没有可流通资产。完整54关能载入不等于54关都做过趣味或数值验收。

`-prototype-smoke` 启动参数会建立三队实验场景，运行真实图形Player，输出 `Builds/Windows/Evidence/three-teams-宽x高.png` 与状态摘要后退出；这是渲染回归，不是陌生玩家试玩。截图需使用可见窗口；本机隐藏窗口返回黑图或捕获失败，不计为渲染通过。

原型API依据Unity官方 [GUI](https://docs.unity3d.com/6000.6/Documentation/ScriptReference/GUI.html)、[系统字体](https://docs.unity3d.com/6000.6/Documentation/ScriptReference/Font.CreateDynamicFontFromOSFont.html)、[实际帧截图](https://docs.unity3d.com/6000.6/Documentation/ScriptReference/ScreenCapture.CaptureScreenshot.html)。
