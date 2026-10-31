## MODIFIED Requirements

### Requirement: Enter an explicit desktop picking session

系统 SHALL 通过当前可见的识别页或首次引导选窗步骤的选择按钮进入桌面拾取模式，暂时隐藏主窗口并显示点击确认和 Esc 取消提示。拾取期间 SHALL 保留当前目标及静态缩略图，不因悬停候选改变目标、切换跟踪或启动候选捕获。选择命令 SHALL 防止重入，拾取和预览获取期间 SHALL 阻止用户启动识别或区域编辑。拾取完成、取消或失败后 SHALL 恢复主窗口原来的位置及正常 / 最大化状态。

#### Scenario: Start picking with an existing selection
- **WHEN** 用户已有目标和缩略图并点击重新选择窗口
- **THEN** 主窗口暂时隐藏并进入拾取模式，原目标、图片及未结束的识别会话保持有效，候选悬停不触发 Attach、WGC、OCR 或 LLM

#### Scenario: Cancel picking
- **WHEN** 用户在确认前按 Esc，且原目标仍然有效
- **THEN** 系统结束拾取并恢复主窗口，保留原目标和原缩略图，不因取消而终止原识别会话

#### Scenario: Reject overlapping operations
- **WHEN** 一次拾取或预览获取尚未完成时再次触发选择、识别或区域编辑命令
- **THEN** 系统不创建并行的拾取、预览、识别或区域编辑操作

#### Scenario: Fail to initialize the picker
- **WHEN** 输入 hook 或拾取辅助窗口无法初始化
- **THEN** 系统清理已经创建的资源，恢复主窗口及仍然有效的原选择，显示可重试的本地化提示并记录英文诊断

#### Scenario: Pick a window from onboarding
- **WHEN** 用户在引导第三步点击选择或重新选择窗口
- **THEN** 应用隐藏同一宿主窗口，并在确认、取消或失败后恢复原来的引导步骤及正常 / 最大化状态；后台识别页不重复处理宿主恢复

### Requirement: Animate the captured image into the recognition page

预览成功时，系统 SHALL 使用该帧的图片副本，从捕获时对应的客户区屏幕位置缩小并沿轻微弧线飞入已恢复主窗口的预览区域，完成后给予轻微落入反馈并显示来自同一帧的静态缩略图。正常动画时长 SHALL 约为 400 毫秒。引导期间 SHALL 以当前可见的引导目标卡片作为终点；常规使用时 SHALL 以识别页目标卡片作为终点，任一时刻仅有一个活动呈现者。系统 SHALL 保持真实目标窗口的位置和大小不变，飞行呈现 SHALL 原生点击穿透且不可激活。页面布局有效后 SHALL 测量实际图片终点，并在需要时将卡片滚动到可见位置。

#### Scenario: Complete the image flight
- **WHEN** 本次选择取得有效帧且识别页预览区域可见
- **THEN** 该帧副本从源客户区位置飞入正方形预览区域，结束后同帧缩略图保持显示，目标应用窗口不被移动或缩放

#### Scenario: Reach a preview outside the previous scroll position
- **WHEN** 主窗口恢复时目标卡片在滚动区域之外
- **THEN** 系统先将卡片滚动到可见位置并取得有效布局，再计算动画终点

#### Scenario: Interrupt the animation
- **WHEN** 飞行过程中用户重新选择、切换页面、隐藏或关闭主窗口，或者显示器布局改变
- **THEN** 系统停止旧动画并释放临时窗口；仍有效的已提交选择显示最终静态结果，旧动画完成回调不能修改新状态

#### Scenario: Degrade only the presentation
- **WHEN** 主窗口无法激活、有效动画终点不可获得或动画呈现失败
- **THEN** 系统恢复主窗口可见状态并直接显示最终预览，不撤销目标或把动画失败作为捕获失败

#### Scenario: Present the preview on the onboarding surface
- **WHEN** 引导第三步选窗取得有效预览且引导目标卡片可见
- **THEN** 系统将同一帧副本飞入引导预览区域，不向隐藏的识别页播放第二次动画，也不因最终交接重新获取预览

## ADDED Requirements

### Requirement: Preserve one selected target across onboarding handoff
引导和常规识别页 SHALL 组合使用同一个聚焦的目标选择与预览模块，共用目标选择、代次、跟踪和当前静态预览状态；引导流程 SHALL 不依赖包含区域编辑和自动识别等行为的完整识别页 ViewModel。应用 SHALL 管理共享模块的最终生命周期。离开引导选窗步骤 SHALL 结束该视图的临时呈现和订阅，但在目标仍有效时 SHALL NOT 清空已提交选择或当前预览。进入主界面 SHALL 不创建第二套选择 / 捕获服务，不因页面挂载重新截图。预览清理 SHALL 由发起操作的模块按所有权规则处理，并在允许识别前完成；晚到回调 SHALL 不覆盖新状态或停止交接后的识别捕获。

#### Scenario: Move from target selection to final confirmation and recognition
- **WHEN** 用户确认引导目标、进入第四步并开始识别
- **THEN** 主界面继续使用同一目标及有效静态预览，通过明确的开始识别入口调用既有识别用例启动捕获；没有重复拾取、预览获取或飞入

#### Scenario: Tear down an outgoing target view
- **WHEN** 引导目标卡片卸载，常规识别卡片随后挂载
- **THEN** 旧视图停止动画并解除事件订阅，当前选择和预览仍由共享状态持有，新视图不会因错过旧预览事件而丢失图片

#### Scenario: Return from final confirmation to target selection
- **WHEN** 用户从第四步返回第三步，且原目标仍有效
- **THEN** 目标卡片读取共享模块中同一选择及预览，不因重新挂载而拾取、截图或启动识别

#### Scenario: Close during an onboarding selection
- **WHEN** 应用在引导拾取、预览或飞入期间退出
- **THEN** 系统按已有契约拆除输入 hook、关闭辅助窗口并取消捕获，旧回调不向已关闭视图发布结果
