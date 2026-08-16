# window-word-overlay Specification

## Purpose
在目标窗口上方显示透明置顶覆盖层，绘制词下划线与 phrase group 标记；本变更在其上叠加振假名渲染。

## ADDED Requirements

### Requirement: 覆盖层内振假名的生命周期
系统 SHALL 在覆盖层渲染会话时，与词下划线一起绘制振假名文本；刷新与隐藏时 SHALL 与下划线一起清除，不留残留。振假名使用与下划线一致的屏幕坐标与 DPI 映射。

#### Scenario: 与下划线一起渲染振假名
- **WHEN** 覆盖层渲染一个包含汉字词的会话
- **THEN** 每个汉字词的振假名与它的下划线在同一渲染批次中出现

#### Scenario: 刷新与隐藏时清除振假名
- **WHEN** 覆盖层以新词刷新或隐藏
- **THEN** 所有已绘制的振假名随下划线一起清除，不留残留