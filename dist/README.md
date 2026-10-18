# dist/

本目录里的 `JunoBridge.dll` **随仓库提交**（见 `.gitignore` 的 `!dist/JunoBridge.dll`）。

原因：模组编译时引用游戏本体的程序集（`SimpleRockets2_Data/Managed/*.dll`），
那些是有版权的游戏文件，不能放进 CI。所以 DLL 由开发者本机 `build.ps1` 产出后
提交进仓库，CI 再用 `package.ps1 -SkipBuild` 打「解压即用」的分发包。

其余产物（zip、SHA256SUMS）不提交，由 CI 生成。