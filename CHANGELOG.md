# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic Versioning](http://semver.org/spec/v2.0.0.html).

## [0.9.0] - 2026-04-14

### Added

- 基础功能，具体参考README文档

## [0.9.1] - 2026-04-16

### Fixed

- 修复Core包更新导致的示例错误

## [0.9.2] - 2026-05-21

### Added

- 添加 接收远程设备输入与信息解析注入

## [0.9.3] - 2026-09-14

### Added

- 新增仓储模拟模块 `WarehouseSimulation`（输送线地图编辑器、离散事件仿真、回放与控制面板等）及 Sample「WarehouseSimulation」
- 并入 ScriptAnimation 模块（Timeline 脚本动画：路网、叉车、堆垛机、机械臂、开箱、缠膜等）
- 新增 Sample「ScriptAnimation」（含 OpenBox / FilmWrapping 子场景）
- `package.json` 声明对 Core、DigitalTwin、Timeline 的依赖
- 添加用于同步 GitCode 的 GitHub Actions 工作流

### Changed

- 整理包内布局：实训相关能力归入 `SimulationPracticum`，远程输入、路网导航、参数化货架等模块目录与程序集对齐
- 优化仓库模拟输送线地图编辑器
- 仓储模拟命名空间与程序集整理（合并拆分过细的 asmdef，统一到 `WarehouseSimulation`）
- 文档目录由 `~document` 调整为 `Documentation~`；补充 ScriptAnimation 模块说明

### Fixed

- 修复布局调整后的程序集引用问题
