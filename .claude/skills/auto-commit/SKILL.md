---
name: auto-commit
description: 当agent执行结束时，自动提交git更改。适用于git仓库中agent执行结束时自动提交git更改。
---

当agent执行结束时，自动提交git更改。

## 使用场景
- 在完成代码修改后自动创建git提交
- 确保工作成果得到版本控制
- 生成符合项目规范的提交信息

## 执行步骤
1. 检查修改代码，若出现以下问题则提示具体问题并拒绝提交:
    - 逻辑错误
    - 拼写错误
    - 边界条件错误
    - 安全漏洞
    - 资源泄漏
    - 敏感信息,如密码/用户名/Secret/Key/Token/等
2. 检查并暂存更改
   - 运行 `git status --porcelain`，如无更改则跳过
   - 运行 `git add .` 暂存所有更改

3. 生成提交信息并提交
   - 分析更改内容，确定类型（feat/fix/refactor/docs/style/test/chore）
   - 生成简洁的提交信息（类型: 描述），使用中文
   - 执行 `git commit -m "提交信息"`

4. 确认提交
   - 运行 `git log -1 --oneline` 显示最新提交

## 注意事项
- 提交log必须为中文,不要出现类似:Co-authored-by
- 不要提交敏感文件（.env, credentials等）
- 提交信息要简洁明了，描述本次修改的目的
- 遵循项目现有的提交信息格式
- 仅在有实质性更改时才提交