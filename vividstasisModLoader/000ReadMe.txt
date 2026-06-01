【中文说明】

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

请注意：程序会先进行字符串修补，再进行代码修补。
所以如果你通过 *.xlsx 修补了字符串，就应该把 "Find" 的值改为修补后的文本。

关于IPC模式：
目前仅适用于与 "BVOClient(Better Vivid/stasis Opreation Client)" 使用

【English】

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

Please note: the program patches strings before patching codes.
So if you patched strings with *.xlsx, you should change the "Find" value to the modified text.

For IPC Mode:
Currently only for "BVOClient(Better Vivid/stasis Opreation Client)" use