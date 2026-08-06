using System.Runtime.CompilerServices;

// 允许平台测试项目访问内部类型（MeikiOcrEngine 等），以便做端到端黄金测试。
[assembly: InternalsVisibleTo("KotobaSenpai.Platform.Windows.Tests")]