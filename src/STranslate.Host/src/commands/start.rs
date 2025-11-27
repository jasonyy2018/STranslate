use clap::{ArgMatches, ValueEnum};
use std::error::Error;
use std::process::{Command as ProcessCommand, Stdio};
use std::thread;
use std::time::Duration;

#[derive(Clone, Debug, ValueEnum)]
pub enum StartMode {
    /// 直接启动进程
    Direct,
    /// 直接提权启动进程
    Elevated,
    /// 执行指定名称的任务计划程序
    Task,
}

pub fn handle_start_command(matches: &ArgMatches) -> Result<(), Box<dyn Error>> {
    let mode = matches.get_one::<StartMode>("mode").unwrap();
    let target = matches.get_one::<String>("target").unwrap();
    let args: Vec<&String> = matches
        .get_many::<String>("args")
        .unwrap_or_default()
        .collect();
    let delay = *matches.get_one::<u64>("delay").unwrap();
    let verbose = matches.get_flag("verbose");

    if verbose {
        println!("🚀 准备启动程序...");
        println!("   启动方式: {:?}", mode);
        println!("   目标: {}", target);
        if !args.is_empty() {
            println!("   参数: {:?}", args);
        }
        if delay > 0 {
            println!("   延迟: {} 秒", delay);
        }
    }

    if delay > 0 {
        if verbose {
            println!("⏳ 延迟 {} 秒后启动...", delay);
        }
        thread::sleep(Duration::from_secs(delay));
    }

    match mode {
        StartMode::Direct => {
            start_direct_process(target, &args, verbose)?;
        }
        StartMode::Elevated => {
            start_elevated_process(target, &args, verbose)?;
        }
        StartMode::Task => {
            start_task_scheduler(target, verbose)?;
        }
    }

    println!("✅ 启动完成!");
    Ok(())
}

fn start_direct_process(
    target: &str,
    args: &[&String],
    verbose: bool,
) -> Result<(), Box<dyn Error>> {
    if verbose {
        println!("🚀 直接启动进程: {}", target);
    }

    #[cfg(target_os = "windows")]
    {
        let mut cmd_args = vec![
            "-Command".to_string(),
            format!("Start-Process '{}'", target),
        ];

        if !args.is_empty() {
            let args_str = args
                .iter()
                .map(|s| s.as_str())
                .collect::<Vec<_>>()
                .join(" ");
            cmd_args[1] = format!("Start-Process '{}' -ArgumentList '{}' ", target, args_str);
        }

        let mut command = ProcessCommand::new("powershell");
        command.args(&cmd_args);

        let output = command.output()?;

        if !output.status.success() {
            let error = String::from_utf8_lossy(&output.stderr);
            if verbose {
                println!("⚠️  进程启动失败: {}", error);
            }
        } else if verbose {
            println!("✅ 进程已启动: {}", target);
        }
    }

    Ok(())
}

fn start_elevated_process(
    target: &str,
    args: &[&String],
    verbose: bool,
) -> Result<(), Box<dyn Error>> {
    if verbose {
        println!("🔑 以提权方式启动进程: {}", target);
    }

    #[cfg(target_os = "windows")]
    {
        let mut cmd_args = vec![
            "-Command".to_string(),
            format!("Start-Process '{}' -Verb RunAs", target),
        ];

        if !args.is_empty() {
            let args_str = args
                .iter()
                .map(|s| s.as_str())
                .collect::<Vec<_>>()
                .join(" ");
            cmd_args[1] = format!(
                "Start-Process '{}' -ArgumentList '{}' -Verb RunAs",
                target, args_str
            );
        }

        let mut command = ProcessCommand::new("powershell");
        command.args(&cmd_args);

        if !verbose {
            command.stdout(Stdio::null()).stderr(Stdio::null());
        }

        command.spawn()?;
    }

    Ok(())
}

fn start_task_scheduler(task_name: &str, verbose: bool) -> Result<(), Box<dyn Error>> {
    if verbose {
        println!("📅 启动任务计划: {}", task_name);
    }

    #[cfg(target_os = "windows")]
    {
        let output = ProcessCommand::new("schtasks")
            .args(&["/Run", "/TN", task_name])
            .output()?;

        if !output.status.success() {
            let error = String::from_utf8_lossy(&output.stderr);
            return Err(format!("启动任务计划失败: {}", error).into());
        }

        if verbose {
            println!("✅ 任务计划已启动: {}", task_name);
        }
    }

    Ok(())
}
