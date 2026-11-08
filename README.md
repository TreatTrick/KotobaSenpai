<div align="center">

<img src="src/KotobaSenpai.App/Assets/Branding/app-icon.png" width="112" alt="KotobaSenpai app icon">

# KotobaSenpai

### The words stay on screen. So do you.

An AI reading companion for Japanese visual novels. Furigana, pitch accents, contextual explanations, and sentence translations appear beside the lines you are reading.

**English** · [简体中文](README.zh-CN.md)

[Features](#-why-kotobasenpai) · [First run](#-a-simple-first-run) · [Build from source](#-build-from-source)

[![Windows desktop](https://img.shields.io/badge/Windows-Desktop-0078D4?logo=windows&logoColor=white)](#-build-from-source) [![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/) [![WPF](https://img.shields.io/badge/UI-WPF-68217A)](https://learn.microsoft.com/dotnet/desktop/wpf/)

<img src="docs/assets/overlay-reading-demo.gif" width="820" alt="KotobaSenpai overlaying furigana and pitch accents on Japanese visual-novel text">

</div>

## ✨ Why KotobaSenpai?

### 1. 🌍 Read Japanese in ANY LANGUAGE YOU CHOOSE

**There is no fixed target-language list.** Choose any left-to-right language supported by your AI model for contextual explanations and sentence translations. Set this language independently from the app interface. Available languages and output quality depend on the AI provider and model.

**The six languages below are just examples.** Click a screenshot to view it at full size.

<table>
  <tr>
    <td width="50%" align="center" valign="top">
      <strong>English</strong><br>
      <a href="docs/assets/multi-language-en.png"><img src="docs/assets/multi-language-en.png" width="100%" alt="Japanese visual-novel text with contextual explanations in English"></a>
    </td>
    <td width="50%" align="center" valign="top">
      <strong>简体中文</strong><br>
      <a href="docs/assets/multi-language-cn.png"><img src="docs/assets/multi-language-cn.png" width="100%" alt="Japanese visual-novel text with contextual explanations in Simplified Chinese"></a>
    </td>
  </tr>
  <tr>
    <td width="50%" align="center" valign="top">
      <strong>한국어</strong><br>
      <a href="docs/assets/multi-language-ko.png"><img src="docs/assets/multi-language-ko.png" width="100%" alt="Japanese visual-novel text with contextual explanations in Korean"></a>
    </td>
    <td width="50%" align="center" valign="top">
      <strong>Español</strong><br>
      <a href="docs/assets/multi-language-es.png"><img src="docs/assets/multi-language-es.png" width="100%" alt="Japanese visual-novel text with contextual explanations in Spanish"></a>
    </td>
  </tr>
  <tr>
    <td width="50%" align="center" valign="top">
      <strong>Português (Brasil)</strong><br>
      <a href="docs/assets/multi-language-pt.png"><img src="docs/assets/multi-language-pt.png" width="100%" alt="Japanese visual-novel text with contextual explanations in Brazilian Portuguese"></a>
    </td>
    <td width="50%" align="center" valign="top">
      <strong>Русский</strong><br>
      <a href="docs/assets/multi-language-ru.png"><img src="docs/assets/multi-language-ru.png" width="100%" alt="Japanese visual-novel text with contextual explanations in Russian"></a>
    </td>
  </tr>
</table>

### 2. 👀 Keep readings on the game text

Furigana appears above kanji, with pitch-accent patterns marked alongside the reading when available. The annotations stay over the visual novel, so you can keep your eyes on the scene instead of moving between the game and a dictionary window.

<img src="docs/assets/furigana-appears.gif" width="760" alt="Furigana appearing above Japanese visual-novel text">

### 3. 🧩 Get one meaning for this scene, not a wall of dictionary senses

KotobaSenpai uses the surrounding sentence to explain words and meaningful grammar patterns, including expressions split apart by other words, such as `A ... B`. It picks the contextual meaning that fits the scene and can return a full-sentence translation as part of the same analysis.

<img src="docs/assets/contextual-analysis-en.gif" width="760" alt="A split Japanese phrase highlighted with its contextual explanation and sentence translation">

### 4. 🤖 Let your local Agent handle API setup

Copy KotobaSenpai's setup prompt into a local coding Agent such as Codex, Claude Code, or Cursor. After you provide the provider details and API key, the Agent can discover models, validate the connection, and apply the configuration for you. You stay in control of the credentials and provider you use.

<img src="docs/assets/agent-assisted-setup-en.gif" width="760" alt="Configuring an AI provider with a local coding Agent">

### 5. 💸 Low token use, low cost

One real-use reference: about **US$0.075** for **30 minutes** of 魔法使いの夜 (_Witch on the Holy Night_) with **DeepSeek V4.1 Flash**. Actual cost varies with provider pricing, model, and token usage; this is an example, not a fixed rate.

### 6. 🧭 A guided first run, without a maze of settings

Onboarding walks you through the essentials: preparing the Japanese dictionary, configuring an AI model, and selecting the visual-novel window. The main controls stay focused on choosing a window, starting recognition, and enabling automatic recognition.

<img src="docs/assets/first-run-onboarding-en.gif" width="760" alt="KotobaSenpai's guided first-run setup">

### 7. 🪟 Built for Windows desktop

KotobaSenpai is a Windows desktop app built with **.NET 10** and **WPF**. Its focused interface and in-window overlay are designed to keep reading responsive and unobtrusive.

## 🚀 A simple first run

1. Prepare the Japanese dictionary in onboarding.
2. Configure your AI provider manually or ask a local Agent to do it with you.
3. Select the game window and start reading.

## 🐉 Thanks for reading

The dragon maid is celebrating your next chapter.

<p align="center"><img src="docs/assets/dragon-maid-jump.gif" width="170" alt="The KotobaSenpai dragon maid jumping for joy"></p>
