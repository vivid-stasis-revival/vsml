VSML-Cross
==========

用法：
  VSML-Cross --data <路径>
  VSML-Cross --data <路径> --overwrite
  VSML-Cross --data <路径> --mods <模组目录> --app-logs <日志目录>

输入文件必须是 data.win、game.droid 或 game.ios。

若不使用 --overwrite 选项，源文件将不会被修改。修补后的文件将
以 data/game-YYYYMMDDHHMMSS.win 的形式保存在源文件旁边。

使用 --overwrite 或 --ow 选项时，将就地修补输入文件。

使用 --mods 选项指定模组目录。若未指定，模组将从
可执行文件旁边的 mods 目录中读取。
使用 --app-logs 选择日志目录。--log-dir 也可作为
别名使用。若未指定，日志将写入可执行文件旁边的 app-logs 目录中。

GameSpecificData 已嵌入 VSML-Cross 程序集，无需
外部 GameSpecificData 目录。

此跨平台纯数据构建不会复制原始文件。

跨平台构建不具备 BVO/TVOClient IPC 功能。

关于 codepatches.json：
  Entry：代码条目名称。
  Find：用于类型 0 或 1 的替换文本。
  Value：替换或插入的代码。
  类型 0：替换所有匹配项。
  类型 1：替换第一个匹配项。
  类型 2：插入到 Function 之前，若 Function 为空则插入到末尾。
  类型 3：插入到 Function 之后，若 Function 为空则插入到开头。
  ExternalFile：codepatches 中的文件，其内容将替换 Value。

字符串修补将在代码修补之前进行。当字符串
修补更改了代码修补所使用的文本时，请更新 Find 的值。

VSML-Cross
==========

Usage:
  VSML-Cross --data <path>
  VSML-Cross --data <path> --overwrite
  VSML-Cross --data <path> --mods <mods-directory> --app-logs <log-directory>

The input file must be data.win, game.droid, or game.ios.

Without --overwrite, the source file is never modified. The patched file is
written next to it as data/game-YYYYMMDDHHMMSS.win.

With --overwrite or --ow, the input file is patched in place.

Use --mods to select the mod directory. Without it, mods are read from the
mods directory next to the executable.
Use --app-logs to select the log directory. --log-dir is also accepted as an
alias. Without it, logs are written to app-logs next to the executable.

GameSpecificData is embedded in the VSML-Cross assembly and does not require
an external GameSpecificData directory.

Raw files are not copied by this cross-platform data-only build.

The cross-platform build has no BVO/TVOClient IPC functionality.

For codepatches.json:
  Entry: code entry name.
  Find: text to replace for Type 0 or 1.
  Value: replacement or inserted code.
  Type 0: replace all matches.
  Type 1: replace the first match.
  Type 2: insert before Function, or at the end when Function is empty.
  Type 3: insert after Function, or at the beginning when Function is empty.
  ExternalFile: file in codepatches whose contents replace Value.

Strings are patched before code patches. Update Find values when a string
patch changes the text used by a code patch.
