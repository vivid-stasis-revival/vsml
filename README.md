## 简介

- 这个工具的原作者并不是我，我只是暂时帮忙维护这个工具。
- 这个工具使用了[Underanalyzer](https://github.com/UnderminersTeam/Underanalyzer)]和[UndertaleModTool](https://github.com/UnderminersTeam/UndertaleModTool)的部分模块，所以也请支持他们！

## Description

- The origin author of this tool wasn't me, it just temporarily maintained by me.
- This tool used parts codes of [Underanalyzer](https://github.com/UnderminersTeam/Underanalyzer) and [UndertaleModTool](https://github.com/UnderminersTeam/UndertaleModTool), please support them as well!

## 使用说明

对于 /excel 中的每个 *.xlsx：A 列必须是原文，B 列必须是修改后的文本。

对于 /raw 中的文件：会用同名文件替换游戏目录中的对应文件。

关于 codepatches.json：
1. Entry：代码条目名称。
2. Find：仅在 Type 为 0 或 1 时使用。
3. Value：当 Type 为 0 或 1 时，表示替换后的代码；当 Type 为 2 或 3 时，表示将要插入的代码。
4. Type：
        0：将找到的所有字符串都替换为 Value。
        1：将找到的第一个字符串替换为 Value。
        2：在 Function 前插入 Value；当 Function 为空时，Value 会插入到该 Entry 末尾。
        3：在 Function 后插入 Value；当 Function 为空时，Value 会插入到该 Entry 开头。
5. Function：目标函数名称，仅在 Type 为 2 或 3 时使用。
6. ExternalFile：/codepatches 中的文件名；若指定该项，则会忽略 Value。
7. OnMiss：可选，锚点未命中（Entry 不存在、Find 找不到、Function 找不到）时的处理级别，缺省为 Warn。
        "Notice" 或 0：提醒。本来就是可选补丁，未命中在预期之内，只记一行日志。
        "Warn" 或 1：警告。默认级别，跳过这一条，其余补丁继续。
        "Error" 或 2：强制错误。这条补丁是模组能正常工作的前提，未命中即中止整个修补流程。

未命中通常意味着锚点被其它模组改掉了，或者游戏更新后代码变了 —— 该补丁不会生效，但不会报错退出。
每个模组处理完还会输出一行汇总（几条生效、几条未生效），便于快速确认有没有补丁悄悄失效。

以下情况与 OnMiss 无关，一律按强制错误中止，因为它们只可能是补丁本身写错了：
codepatches.json 无法解析、缺少 Entry、Type 不在 0-3 范围内、Type 为 0/1 但 Find 为空、
ExternalFile 指向的文件不存在、目标条目反编译失败、目标条目是匿名函数引用。
中止发生在写回 data.win 之前，因此游戏文件仍保持在还原备份后的干净状态。

## Use Guide

For each *.xlsx in /excel: column A must be the original text, and column B must be the modified text.

For files in /raw: every file with the same name in the game folder will be replaced.

For codepatches.json:
1. Entry: name of the code entry.
2. Find: only used when Type is 0 or 1.
3. Value: when Type is 0 or 1, this means the modified code; when Type is 2 or 3, this means the code to be inserted.
4. Type:
        0: Replace all found strings with Value.
        1: Replace the first found string with Value.
        2: Insert Value before Function; when Function is empty, Value will be inserted at the end of the entry.
        3: Insert Value after Function; when Function is empty, Value will be inserted at the beginning of the entry.
5. Function: name of the target function, only used when Type is 2 or 3.
6. ExternalFile: name of the file in /codepatches; if this is specified, Value will be ignored.
7. OnMiss: optional. How to react when the anchor is not matched (Entry does not exist, Find not found, Function not found). Defaults to Warn.
        "Notice" or 0: the patch is optional and a miss is expected; just log one line.
        "Warn" or 1: the default. Skip this patch and keep going with the rest.
        "Error" or 2: this patch is required for the mod to work; a miss aborts the whole patching run.

A miss usually means another mod already changed the anchor, or the game was updated and the code moved.
The patch simply does not take effect. A summary line per mod (how many applied, how many did not) is printed
at the end so silently dead patches are easy to spot.

The following are always fatal regardless of OnMiss, because they can only mean the patch itself is wrong:
codepatches.json fails to parse, a missing Entry, a Type outside 0-3, an empty Find with Type 0 or 1,
an ExternalFile that does not exist, a target entry that fails to decompile, or a target entry that is an
anonymous function reference. The abort happens before data.win is written back, so the game files are left
in the clean state they had after the backup was restored.

Please note: the program patches strings before patching codes.
So if you patched strings with *.xlsx, you should change the "Find" value to the modified text.