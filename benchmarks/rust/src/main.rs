use std::env;
use std::fs;
use std::path::{Path, PathBuf};
use std::process::ExitCode;
use std::time::Instant;

use sudoku_arrow_rust_bench::{
    parse_iss_arrow_text, touch_tables, ArrowSudokuSolver, SolveOptions, TraceEntry, NUM_CELLS,
    SIZE,
};

fn main() -> ExitCode {
    touch_tables();

    let options = match BenchmarkOptions::parse(env::args().skip(1)) {
        Ok(options) => options,
        Err(err) => {
            eprintln!("{err}\n");
            BenchmarkOptions::print_usage();
            return ExitCode::from(2);
        }
    };

    if options.show_help {
        BenchmarkOptions::print_usage();
        return ExitCode::SUCCESS;
    }

    let iss_text = match fs::read_to_string(&options.puzzle_path) {
        Ok(text) => text,
        Err(err) => {
            eprintln!(
                "Failed to read puzzle {}: {err}",
                options.puzzle_path.display()
            );
            return ExitCode::from(2);
        }
    };

    match run_samples(&iss_text, &options) {
        Ok(output) => {
            println!("{}", output.to_json());
            ExitCode::SUCCESS
        }
        Err(err) => {
            eprintln!("{err}");
            ExitCode::from(1)
        }
    }
}

struct BenchmarkOptions {
    puzzle_path: PathBuf,
    warmup: usize,
    samples: usize,
    max_solutions: usize,
    trace_limit: usize,
    show_help: bool,
}

impl BenchmarkOptions {
    fn parse(args: impl Iterator<Item = String>) -> Result<Self, String> {
        let mut puzzle_path = find_default_puzzle_path();
        let mut warmup = 5usize;
        let mut samples = 20usize;
        let mut max_solutions = 0usize;
        let mut trace_limit = 0usize;

        let mut args = args.peekable();
        while let Some(arg) = args.next() {
            if arg == "--help" || arg == "-h" {
                return Ok(Self {
                    puzzle_path,
                    warmup,
                    samples,
                    max_solutions,
                    trace_limit,
                    show_help: true,
                });
            }

            let (name, value) = if let Some((name, value)) = arg.split_once('=') {
                (name.to_string(), value.to_string())
            } else {
                let Some(value) = args.next() else {
                    return Err(format!("Missing value for {arg}."));
                };
                (arg, value)
            };

            match name.as_str() {
                "--puzzle" => puzzle_path = PathBuf::from(value),
                "--warmup" => warmup = parse_non_negative(&name, &value)?,
                "--samples" => samples = parse_non_negative(&name, &value)?.max(1),
                "--max-solutions" => max_solutions = parse_non_negative(&name, &value)?,
                "--trace-limit" => trace_limit = parse_non_negative(&name, &value)?,
                _ => return Err(format!("Unknown argument: {name}")),
            }
        }

        let full_puzzle_path = puzzle_path
            .canonicalize()
            .map_err(|_| format!("Puzzle file not found: {}", puzzle_path.display()))?;

        Ok(Self {
            puzzle_path: full_puzzle_path,
            warmup,
            samples,
            max_solutions,
            trace_limit,
            show_help: false,
        })
    }

    fn print_usage() {
        eprintln!(
            "Usage: cargo run --release --manifest-path benchmarks/rust/Cargo.toml -- [options]"
        );
        eprintln!();
        eprintln!("Options:");
        eprintln!("  --puzzle <path>          ISS .Arrow text puzzle. Defaults to iss-arrow-data.txt found above the cwd.");
        eprintln!("  --warmup <count>         Warmup solves, default 5.");
        eprintln!("  --samples <count>        Measured solves, default 20.");
        eprintln!("  --max-solutions <count>  Stop after count solutions, 0 = unlimited.");
        eprintln!(
            "  --trace-limit <count>    Capture first count branch trace entries, default 0."
        );
    }
}

fn parse_non_negative(name: &str, value: &str) -> Result<usize, String> {
    value
        .parse::<usize>()
        .map_err(|_| format!("{name} must be a non-negative integer: {value}"))
}

fn find_default_puzzle_path() -> PathBuf {
    let mut directory = env::current_dir().ok();
    while let Some(current) = directory {
        let candidate = current.join("iss-arrow-data.txt");
        if candidate.exists() {
            return candidate;
        }
        directory = current.parent().map(Path::to_path_buf);
    }

    let manifest_candidate = PathBuf::from(env!("CARGO_MANIFEST_DIR"))
        .join("..")
        .join("..")
        .join("iss-arrow-data.txt");
    if manifest_candidate.exists() {
        return manifest_candidate;
    }

    PathBuf::from("iss-arrow-data.txt")
}

fn run_samples(iss_text: &str, options: &BenchmarkOptions) -> Result<BenchmarkOutput, String> {
    let solve_options = SolveOptions {
        max_solutions: options.max_solutions,
        trace_limit: options.trace_limit,
        ..SolveOptions::default()
    };

    for _ in 0..options.warmup {
        let _ = run_solve(iss_text, solve_options)?;
    }

    let mut samples = Vec::with_capacity(options.samples);
    for _ in 0..options.samples {
        samples.push(run_solve(iss_text, solve_options)?);
    }

    let summary = summarize_samples(&samples);
    Ok(BenchmarkOutput {
        kind: "arrow-rust-native-samples".to_string(),
        warmup: options.warmup,
        samples,
        summary,
        environment: EnvironmentInfo {
            rustc: option_env!("RUSTC_VERSION").unwrap_or("rustc").to_string(),
            target: format!("{}-{}", env::consts::ARCH, env::consts::OS),
            processor_count: std::thread::available_parallelism().map_or(1, usize::from),
        },
    })
}

fn run_solve(iss_text: &str, options: SolveOptions) -> Result<SampleResult, String> {
    let setup_start = Instant::now();
    let puzzle = parse_iss_arrow_text(iss_text)?;
    let mut solver = ArrowSudokuSolver::new(&puzzle, options)?;
    let setup_ms = elapsed_ms(setup_start);

    let runtime_start = Instant::now();
    let solutions = solver.count_solutions(options.max_solutions)?;
    let runtime_ms = elapsed_ms(runtime_start);

    let stats = solver.result_stats();
    Ok(SampleResult {
        engine: "rust-arrow-reference".to_string(),
        language: "rust".to_string(),
        runtime: "native".to_string(),
        puzzle: PuzzleInfo {
            size: SIZE,
            arrows: puzzle.arrows.len(),
            givens: 0,
        },
        setup_ms,
        runtime_ms,
        total_ms: setup_ms + runtime_ms,
        solutions,
        guesses: stats.guesses,
        values_tried: stats.values_tried,
        nodes_searched: stats.nodes_searched,
        backtracks: stats.backtracks,
        domain_eliminations: stats.domain_eliminations,
        propagation_steps: stats.propagation_steps,
        trace_hash: stats.trace_hash,
        trace: stats.trace,
    })
}

fn elapsed_ms(start: Instant) -> f64 {
    start.elapsed().as_secs_f64() * 1000.0
}

fn summarize_samples(samples: &[SampleResult]) -> SummaryResult {
    let mut setup = Vec::with_capacity(samples.len());
    let mut runtime = Vec::with_capacity(samples.len());
    let mut total = Vec::with_capacity(samples.len());
    for sample in samples {
        setup.push(sample.setup_ms);
        runtime.push(sample.runtime_ms);
        total.push(sample.total_ms);
    }

    let first = &samples[0];
    SummaryResult {
        setup_ms: summarize_numbers(&mut setup),
        runtime_ms: summarize_numbers(&mut runtime),
        total_ms: summarize_numbers(&mut total),
        solutions: first.solutions,
        guesses: first.guesses,
        values_tried: first.values_tried,
        nodes_searched: first.nodes_searched,
        backtracks: first.backtracks,
        domain_eliminations: first.domain_eliminations,
        propagation_steps: first.propagation_steps,
        trace_hash: first.trace_hash.clone(),
        consistent: samples.iter().all(|sample| {
            sample.solutions == first.solutions
                && sample.guesses == first.guesses
                && sample.values_tried == first.values_tried
                && sample.nodes_searched == first.nodes_searched
                && sample.backtracks == first.backtracks
                && sample.trace_hash == first.trace_hash
        }),
    }
}

fn summarize_numbers(values: &mut [f64]) -> NumberSummary {
    values.sort_by(|a, b| a.total_cmp(b));
    NumberSummary {
        min: values[0],
        p10: percentile(values, 0.10),
        median: percentile(values, 0.50),
        p90: percentile(values, 0.90),
        max: values[values.len() - 1],
    }
}

fn percentile(sorted: &[f64], p: f64) -> f64 {
    if sorted.len() == 1 {
        return sorted[0];
    }
    let index = (sorted.len() - 1) as f64 * p;
    let lower = index.floor() as usize;
    let upper = index.ceil() as usize;
    if lower == upper {
        return sorted[lower];
    }
    let weight = index - lower as f64;
    sorted[lower] * (1.0 - weight) + sorted[upper] * weight
}

struct BenchmarkOutput {
    kind: String,
    warmup: usize,
    samples: Vec<SampleResult>,
    summary: SummaryResult,
    environment: EnvironmentInfo,
}

struct SampleResult {
    engine: String,
    language: String,
    runtime: String,
    puzzle: PuzzleInfo,
    setup_ms: f64,
    runtime_ms: f64,
    total_ms: f64,
    solutions: usize,
    guesses: usize,
    values_tried: usize,
    nodes_searched: usize,
    backtracks: usize,
    domain_eliminations: usize,
    propagation_steps: usize,
    trace_hash: String,
    trace: Vec<TraceEntry>,
}

struct PuzzleInfo {
    size: usize,
    arrows: usize,
    givens: usize,
}

struct NumberSummary {
    min: f64,
    p10: f64,
    median: f64,
    p90: f64,
    max: f64,
}

struct SummaryResult {
    setup_ms: NumberSummary,
    runtime_ms: NumberSummary,
    total_ms: NumberSummary,
    solutions: usize,
    guesses: usize,
    values_tried: usize,
    nodes_searched: usize,
    backtracks: usize,
    domain_eliminations: usize,
    propagation_steps: usize,
    trace_hash: String,
    consistent: bool,
}

struct EnvironmentInfo {
    rustc: String,
    target: String,
    processor_count: usize,
}

impl BenchmarkOutput {
    fn to_json(&self) -> String {
        let mut json = String::new();
        json.push_str("{\n");
        push_str_prop(&mut json, 1, "kind", &self.kind, true);
        push_num_prop(&mut json, 1, "warmup", self.warmup as f64, true);
        push_indent(&mut json, 1);
        json.push_str("\"samples\": [\n");
        for (index, sample) in self.samples.iter().enumerate() {
            json.push_str(&sample.to_json(2));
            if index + 1 != self.samples.len() {
                json.push(',');
            }
            json.push('\n');
        }
        push_indent(&mut json, 1);
        json.push_str("],\n");
        push_indent(&mut json, 1);
        json.push_str("\"summary\": ");
        json.push_str(&self.summary.to_json(1));
        json.push_str(",\n");
        push_indent(&mut json, 1);
        json.push_str("\"environment\": ");
        json.push_str(&self.environment.to_json(1));
        json.push('\n');
        json.push('}');
        json
    }
}

impl SampleResult {
    fn to_json(&self, indent: usize) -> String {
        let mut json = String::new();
        push_indent(&mut json, indent);
        json.push_str("{\n");
        push_str_prop(&mut json, indent + 1, "engine", &self.engine, true);
        push_str_prop(&mut json, indent + 1, "language", &self.language, true);
        push_str_prop(&mut json, indent + 1, "runtime", &self.runtime, true);
        push_indent(&mut json, indent + 1);
        json.push_str("\"puzzle\": ");
        json.push_str(&self.puzzle.to_json(indent + 1));
        json.push_str(",\n");
        push_num_prop(&mut json, indent + 1, "setupMs", self.setup_ms, true);
        push_num_prop(&mut json, indent + 1, "runtimeMs", self.runtime_ms, true);
        push_num_prop(&mut json, indent + 1, "totalMs", self.total_ms, true);
        push_num_prop(
            &mut json,
            indent + 1,
            "solutions",
            self.solutions as f64,
            true,
        );
        push_num_prop(&mut json, indent + 1, "guesses", self.guesses as f64, true);
        push_num_prop(
            &mut json,
            indent + 1,
            "valuesTried",
            self.values_tried as f64,
            true,
        );
        push_num_prop(
            &mut json,
            indent + 1,
            "nodesSearched",
            self.nodes_searched as f64,
            true,
        );
        push_num_prop(
            &mut json,
            indent + 1,
            "backtracks",
            self.backtracks as f64,
            true,
        );
        push_num_prop(
            &mut json,
            indent + 1,
            "domainEliminations",
            self.domain_eliminations as f64,
            true,
        );
        push_num_prop(
            &mut json,
            indent + 1,
            "propagationSteps",
            self.propagation_steps as f64,
            true,
        );
        push_str_prop(&mut json, indent + 1, "traceHash", &self.trace_hash, true);
        push_indent(&mut json, indent + 1);
        json.push_str("\"trace\": [");
        for (index, trace) in self.trace.iter().enumerate() {
            if index == 0 {
                json.push('\n');
            }
            json.push_str(&trace_to_json(trace, indent + 2));
            if index + 1 != self.trace.len() {
                json.push(',');
            }
            json.push('\n');
        }
        if !self.trace.is_empty() {
            push_indent(&mut json, indent + 1);
        }
        json.push_str("]\n");
        push_indent(&mut json, indent);
        json.push('}');
        json
    }
}

impl PuzzleInfo {
    fn to_json(&self, indent: usize) -> String {
        format!(
            "{{\n{pad}\"size\": {},\n{pad}\"arrows\": {},\n{pad}\"givens\": {}\n{close}}}",
            self.size,
            self.arrows,
            self.givens,
            pad = "  ".repeat(indent + 1),
            close = "  ".repeat(indent),
        )
    }
}

impl SummaryResult {
    fn to_json(&self, indent: usize) -> String {
        let mut json = String::new();
        json.push_str("{\n");
        push_indent(&mut json, indent + 1);
        json.push_str("\"setupMs\": ");
        json.push_str(&self.setup_ms.to_json(indent + 1));
        json.push_str(",\n");
        push_indent(&mut json, indent + 1);
        json.push_str("\"runtimeMs\": ");
        json.push_str(&self.runtime_ms.to_json(indent + 1));
        json.push_str(",\n");
        push_indent(&mut json, indent + 1);
        json.push_str("\"totalMs\": ");
        json.push_str(&self.total_ms.to_json(indent + 1));
        json.push_str(",\n");
        push_num_prop(
            &mut json,
            indent + 1,
            "solutions",
            self.solutions as f64,
            true,
        );
        push_num_prop(&mut json, indent + 1, "guesses", self.guesses as f64, true);
        push_num_prop(
            &mut json,
            indent + 1,
            "valuesTried",
            self.values_tried as f64,
            true,
        );
        push_num_prop(
            &mut json,
            indent + 1,
            "nodesSearched",
            self.nodes_searched as f64,
            true,
        );
        push_num_prop(
            &mut json,
            indent + 1,
            "backtracks",
            self.backtracks as f64,
            true,
        );
        push_num_prop(
            &mut json,
            indent + 1,
            "domainEliminations",
            self.domain_eliminations as f64,
            true,
        );
        push_num_prop(
            &mut json,
            indent + 1,
            "propagationSteps",
            self.propagation_steps as f64,
            true,
        );
        push_str_prop(&mut json, indent + 1, "traceHash", &self.trace_hash, true);
        push_bool_prop(&mut json, indent + 1, "consistent", self.consistent, false);
        json.push('\n');
        push_indent(&mut json, indent);
        json.push('}');
        json
    }
}

impl NumberSummary {
    fn to_json(&self, indent: usize) -> String {
        format!(
            "{{\n{pad}\"min\": {},\n{pad}\"p10\": {},\n{pad}\"median\": {},\n{pad}\"p90\": {},\n{pad}\"max\": {}\n{close}}}",
            format_number(self.min),
            format_number(self.p10),
            format_number(self.median),
            format_number(self.p90),
            format_number(self.max),
            pad = "  ".repeat(indent + 1),
            close = "  ".repeat(indent),
        )
    }
}

impl EnvironmentInfo {
    fn to_json(&self, indent: usize) -> String {
        let mut json = String::new();
        json.push_str("{\n");
        push_str_prop(&mut json, indent + 1, "rustc", &self.rustc, true);
        push_str_prop(&mut json, indent + 1, "target", &self.target, true);
        push_num_prop(
            &mut json,
            indent + 1,
            "processorCount",
            self.processor_count as f64,
            false,
        );
        push_indent(&mut json, indent);
        json.push('}');
        json
    }
}

fn trace_to_json(trace: &TraceEntry, indent: usize) -> String {
    let mut json = String::new();
    push_indent(&mut json, indent);
    json.push_str("{\n");
    push_num_prop(&mut json, indent + 1, "depth", trace.depth as f64, true);
    push_str_prop(&mut json, indent + 1, "cell", &trace.cell, true);
    push_num_prop(&mut json, indent + 1, "value", trace.value as f64, true);
    push_str_prop(
        &mut json,
        indent + 1,
        "candidates",
        &trace.candidates,
        false,
    );
    json.push('\n');
    push_indent(&mut json, indent);
    json.push('}');
    json
}

fn push_str_prop(json: &mut String, indent: usize, name: &str, value: &str, comma: bool) {
    push_indent(json, indent);
    json.push('"');
    json.push_str(name);
    json.push_str("\": \"");
    json.push_str(&escape_json(value));
    json.push('"');
    if comma {
        json.push(',');
    }
    json.push('\n');
}

fn push_num_prop(json: &mut String, indent: usize, name: &str, value: f64, comma: bool) {
    push_indent(json, indent);
    json.push('"');
    json.push_str(name);
    json.push_str("\": ");
    json.push_str(&format_number(value));
    if comma {
        json.push(',');
    }
    json.push('\n');
}

fn push_bool_prop(json: &mut String, indent: usize, name: &str, value: bool, comma: bool) {
    push_indent(json, indent);
    json.push('"');
    json.push_str(name);
    json.push_str("\": ");
    json.push_str(if value { "true" } else { "false" });
    if comma {
        json.push(',');
    }
}

fn push_indent(json: &mut String, indent: usize) {
    for _ in 0..indent {
        json.push_str("  ");
    }
}

fn format_number(value: f64) -> String {
    if value.fract() == 0.0 {
        format!("{value:.0}")
    } else {
        format!("{value}")
    }
}

fn escape_json(value: &str) -> String {
    let mut escaped = String::new();
    for ch in value.chars() {
        match ch {
            '"' => escaped.push_str("\\\""),
            '\\' => escaped.push_str("\\\\"),
            '\n' => escaped.push_str("\\n"),
            '\r' => escaped.push_str("\\r"),
            '\t' => escaped.push_str("\\t"),
            ch if ch.is_control() => escaped.push_str(&format!("\\u{:04x}", ch as u32)),
            ch => escaped.push(ch),
        }
    }
    escaped
}

#[allow(dead_code)]
const _: usize = NUM_CELLS;
