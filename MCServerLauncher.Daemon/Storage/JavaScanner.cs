﻿/* ---------------------------------------------------------------------------------------------
   MCServerLauncher Future Java Scanner
   Original Author: LxHTT & AresConnor & Tigercrl
   You can only use this file if you are permitted to do so,
   otherwise you may be prosecuted for violating the law.
   Copyright (c) 2022-2024 MCSLTeam. All rights reserved.
--------------------------------------------------------------------------------------------- */

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using MCServerLauncher.Daemon;
using Newtonsoft.Json;
using Serilog;

namespace MCServerLauncher.Daemon.Storage;

public static class JavaScanner
{
    private const string JavaVersionPattern = @"(\d+)(?:\.(\d+))?(?:\.(\d+))?(?:[._](\d+))?(?:-(.+))?";

    private static readonly List<string> MatchedKeys = new()
    {
        "intellij", "cache", "官启", "vape", "组件", "我的", "liteloader", "运行", "pcl", "bin", "appcode", "untitled folder",
        "content", "microsoft", "program", "lunar", "goland", "download", "corretto", "dragonwell", "客户", "client",
        "新建文件夹", "badlion", "usr", "temp", "ext", "run", "server", "软件", "software", "arctime", "jdk", "phpstorm",
        "eclipse", "rider", "x64", "jbr", "环境", "jre", "env", "jvm", "启动", "未命名文件夹", "sigma", "mojang", "daemon",
        "craft", "oracle", "vanilla", "lib", "file", "msl", "x86", "bakaxl", "高清", "local", "mod", "原版", "webstorm",
        "应用", "hotspot", "fabric", "整合", "net", "mine", "服务", "opt", "home", "idea", "clion", "path", "android",
        "green", "zulu", "官方", "forge", "游戏", "blc", "user", "国服", "pycharm", "3dmark", "data", "roaming", "程序", "java",
        "前置", "soar", "1.", "mc", "世界", "jetbrains", "cheatbreaker", "game", "网易", "launch", "fsm", "root",
        Environment.UserName
    };

    private static readonly List<string> ExcludedKeys = new() { "$", "{", "}", "__", "office" };

    private static Process StartJava(string path)
    {
        ProcessStartInfo info = new()
        {
            FileName = path,
            Arguments = "-version",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        Process javaProcess = new() { StartInfo = info };
        javaProcess.Start();
        return javaProcess;
    }

    private static string RegexJavaVersion(string javaOutput)
    {
        var match = Regex.Match(javaOutput, JavaVersionPattern);
        return match.Success ? match.Value : null;
    }


    private static bool IsMatchedKey(string path)
    {
        return !ExcludedKeys.Any(path.Contains) && MatchedKeys.Any(path.Contains);
    }

    private static List<string> SplitEnvPath()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        return (BasicUtils.IsWindows() ? path.Split(';') : path.Split(':')).ToList();
    }


    private static List<Process> StartScan(string path, bool recursive = true)
    {
        Func<string, bool> matcher = BasicUtils.IsWindows() ? s => s.EndsWith("java.exe") : s => s.EndsWith("java");
        return SingleScanJob(path, matcher, recursive);
    }

    private static List<JavaInfo> Mapper(List<Process> javaProcesses)
    {
        HashSet<JavaInfo> javaInfos = new();
        foreach (var process in javaProcesses)
        {
            process.WaitForExit();
            var content = process.StandardError.ReadToEnd();
            var javaVersion = RegexJavaVersion(content);

            if (javaVersion == null) continue;

            javaInfos.Add(new JavaInfo
            {
                Path = process.StartInfo.FileName,
                Version = javaVersion,
                Architecture = content.Contains("64-Bit") ? "x64" : "x86"
            });
        }

        return javaInfos.ToList();
    }

    private static async Task<List<JavaInfo>> MapperAsync(List<Process> javaProcesses)
    {
        ConcurrentDictionary<JavaInfo, bool> javaInfos = new();
        foreach (var process in javaProcesses)
        {
            await process.WaitForExitAsync();
            var content = await process.StandardError.ReadToEndAsync();
            var javaVersion = RegexJavaVersion(content);

            if (javaVersion == null) continue;

            javaInfos.TryAdd(new JavaInfo
            {
                Path = process.StartInfo.FileName,
                Version = javaVersion,
                Architecture = content.Contains("64-Bit") ? "x64" : "x86"
            }, true);
        }

        return javaInfos.Keys.ToList();
    }

    private static void SingleScanJob(
        string workingPath,
        Func<string, bool> matcher,
        List<Process> javaProcesses,
        bool recursive)
    {
        if (File.Exists(workingPath)) return; // Skip if it is a file
        try
        {
            foreach (var pending in Directory.GetFileSystemEntries(workingPath!))
            {
                var absoluteFilePath = Path.GetFullPath(pending);
                if (File.Exists(absoluteFilePath))
                {
                    if (!matcher(Path.GetFileName(pending))) continue;
                    Log.Debug($"[JVM] Found possible Java \"{absoluteFilePath}\", plan to check it");
                    javaProcesses.Add(StartJava(absoluteFilePath));
                }
                else if (IsMatchedKey(Path.GetFileName(pending).ToLower()) && recursive) // Deliver a deeper search
                {
                    SingleScanJob(absoluteFilePath, matcher, javaProcesses, true);
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (Exception ex)
        {
            Log.Warning($"[JVM] A error occured while searching dir \"{workingPath}\", Reason: {ex.Message}");
        }
    }

    private static List<Process> SingleScanJob(string workingPath, Func<string, bool> matcher, bool recursive)
    {
        List<Process> processes = new();
        SingleScanJob(workingPath, matcher, processes, recursive);
        return processes;
    }

    /// <summary>
    ///     扫描Java,Async版
    /// </summary>
    /// <returns></returns>
    public static async Task<List<JavaInfo>> ScanJavaAsync()
    {
        Log.Debug("[JVM] Start scanning available Java");

        List<Process> pending = new();
        if (BasicUtils.IsWindows())
            for (var i = 65; i <= 90; i++)
            {
                var drive = $"{(char)i}:\\";
                if (Directory.Exists(drive)) pending.AddRange(StartScan(drive));
            }
        else
            pending.AddRange(StartScan("/"));

        // PATH
        SplitEnvPath().ForEach(path =>
        {
            if (Directory.Exists(path)) pending.AddRange(StartScan(path, false));
        });
        var javas = await MapperAsync(pending);

        var cnt = 0;
        foreach (var possibleJavaPath in javas)
        {
            Log.Debug(
                $"[JVM] Found certain Java at: {possibleJavaPath.Path} (Version: {possibleJavaPath.Version})");
            cnt++;
        }

        Console.WriteLine($"Total: {cnt}");
        return javas;
    }

    /// <summary>
    ///     扫描Java
    /// </summary>
    /// <returns></returns>
    public static List<JavaInfo> ScanJava()
    {
        Log.Information("[JVM] Start scanning available Java");

        List<Process> pending = new();

        // disk
        if (BasicUtils.IsWindows())
            for (var i = 65; i <= 90; i++)
            {
                var drive = $"{(char)i}:\\";
                if (Directory.Exists(drive)) pending.AddRange(StartScan(drive));
            }
        else
            pending.AddRange(StartScan("/"));

        
        Log.Debug("[JVM] Scanning disk finished, start scanning PATH");
        
        // PATH
        SplitEnvPath().ForEach(path =>
        {
            if (Directory.Exists(path)) pending.AddRange(StartScan(path, false));
        });

        var javas = Mapper(pending);

        var cnt = 0;
        foreach (var possibleJavaPath in javas)
        {
            Log.Information(
                $"[JVM] Found certain Java at: {possibleJavaPath.Path} (Version: {possibleJavaPath.Version})");
            cnt++;
        }

        Console.WriteLine($"Total: {cnt}");
        return javas;
    }

    /// <summary>
    ///     改用struct: 默认实现了值比较
    /// </summary>
    public struct JavaInfo
    {
        public string Path { get; set; }
        public string Version { get; set; }
        public string Architecture { get; set; }

        public override string ToString()
        {
            return JsonConvert.SerializeObject(this, Formatting.Indented);
        }
    }
}