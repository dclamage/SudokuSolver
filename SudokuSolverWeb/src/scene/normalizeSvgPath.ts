import {
  normalizeNativeDistance,
  normalizeNativePoint,
} from "./normalizeSceneGeometry";
import type {
  NormalizedPoint,
  SceneNormalizationFrame,
} from "./normalizeSceneGeometry";

const SUPPORTED_COMMAND = /^[AaCcHhLlMmQqSsTtVvZz]$/;
const NUMBER_TOKEN = /^[+-]?(?:(?:\d+\.?\d*)|(?:\.\d+))(?:[eE][+-]?\d+)?/;

type PathCommand =
  | "A"
  | "C"
  | "H"
  | "L"
  | "M"
  | "Q"
  | "S"
  | "T"
  | "V"
  | "Z";

type NormalizePathResult =
  | { d: string; error?: never }
  | { d?: never; error: string };

const PARAMETER_COUNTS: Record<Exclude<PathCommand, "Z">, number> = {
  A: 7,
  C: 6,
  H: 1,
  L: 2,
  M: 2,
  Q: 4,
  S: 4,
  T: 2,
  V: 1,
};

class PathScanner {
  private index = 0;

  public constructor(private readonly source: string) {}

  private skipSeparators() {
    while (
      this.index < this.source.length &&
      (/[\s]/.test(this.source[this.index]) || this.source[this.index] === ",")
    ) {
      this.index += 1;
    }
  }

  public atEnd() {
    this.skipSeparators();
    return this.index >= this.source.length;
  }

  public peekIsCommand() {
    this.skipSeparators();
    return /[A-Za-z]/.test(this.source[this.index] ?? "");
  }

  public readCommand() {
    this.skipSeparators();
    const command = this.source[this.index];
    if (command === undefined || !/[A-Za-z]/.test(command)) {
      return undefined;
    }
    this.index += 1;
    return command;
  }

  public readNumber() {
    this.skipSeparators();
    const match = this.source.slice(this.index).match(NUMBER_TOKEN);
    if (match === null) {
      return undefined;
    }
    this.index += match[0].length;
    const value = Number(match[0]);
    return Number.isFinite(value) ? value : undefined;
  }

  public readFlag() {
    this.skipSeparators();
    const flag = this.source[this.index];
    if (flag !== "0" && flag !== "1") {
      return undefined;
    }
    this.index += 1;
    return Number(flag);
  }
}

function sceneNumber(value: number) {
  const integer = Math.round(value);
  const snapped =
    Math.abs(value - integer) <=
    Number.EPSILON * 32 * Math.max(1, Math.abs(value))
      ? integer
      : value;
  const rounded = Number(snapped.toPrecision(15));
  return Object.is(rounded, -0) ? "0" : String(rounded);
}

function readParameters(scanner: PathScanner, command: Exclude<PathCommand, "Z">) {
  const parameters: number[] = [];
  for (let index = 0; index < PARAMETER_COUNTS[command]; index += 1) {
    const value =
      command === "A" && (index === 3 || index === 4)
        ? scanner.readFlag()
        : scanner.readNumber();
    if (value === undefined) {
      return undefined;
    }
    parameters.push(value);
  }
  return parameters;
}

function normalizedPoint(
  x: number,
  y: number,
  relative: boolean,
  current: NormalizedPoint,
  frame: SceneNormalizationFrame,
) {
  return relative
    ? {
        x: current.x + normalizeNativeDistance(x, frame),
        y: current.y + normalizeNativeDistance(y, frame),
      }
    : normalizeNativePoint({ x, y }, frame);
}

function pointsAreFinite(points: readonly NormalizedPoint[]) {
  return points.every(
    (point) => Number.isFinite(point.x) && Number.isFinite(point.y),
  );
}

/** Normalizes supported SVG path data through the stable scene frame. */
export function normalizeSvgPathData(
  d: string,
  frame: SceneNormalizationFrame,
): NormalizePathResult {
  const scanner = new PathScanner(d);
  const output: string[] = [];
  let activeCommand: string | undefined;
  let current = normalizeNativePoint({ x: 0, y: 0 }, frame);
  let subpathStart = current;
  let hasMove = false;

  while (!scanner.atEnd()) {
    if (scanner.peekIsCommand()) {
      activeCommand = scanner.readCommand();
    }
    if (activeCommand === undefined || !SUPPORTED_COMMAND.test(activeCommand)) {
      return { error: "annotation path contains an unsupported or missing command." };
    }

    const relative = activeCommand === activeCommand.toLowerCase();
    const command = activeCommand.toUpperCase() as PathCommand;
    if (!hasMove && command !== "M") {
      return { error: "annotation path must begin with a move command." };
    }
    if (command === "Z") {
      output.push("Z");
      current = subpathStart;
      activeCommand = undefined;
      continue;
    }

    let groupIndex = 0;
    while (true) {
      const parameters = readParameters(scanner, command);
      if (parameters === undefined) {
        return { error: `annotation path has invalid ${command} parameters.` };
      }
      const emittedCommand = command === "M" && groupIndex > 0 ? "L" : command;
      const base = current;
      let next = current;
      let rendered: string;

      switch (emittedCommand) {
        case "M":
        case "L":
        case "T": {
          next = normalizedPoint(
            parameters[0],
            parameters[1],
            relative,
            base,
            frame,
          );
          rendered = `${emittedCommand} ${sceneNumber(next.x)} ${sceneNumber(next.y)}`;
          break;
        }
        case "H": {
          const x = relative
            ? base.x + normalizeNativeDistance(parameters[0], frame)
            : normalizeNativePoint({ x: parameters[0], y: 0 }, frame).x;
          next = { x, y: base.y };
          rendered = `H ${sceneNumber(x)}`;
          break;
        }
        case "V": {
          const y = relative
            ? base.y + normalizeNativeDistance(parameters[0], frame)
            : normalizeNativePoint({ x: 0, y: parameters[0] }, frame).y;
          next = { x: base.x, y };
          rendered = `V ${sceneNumber(y)}`;
          break;
        }
        case "C": {
          const first = normalizedPoint(
            parameters[0],
            parameters[1],
            relative,
            base,
            frame,
          );
          const second = normalizedPoint(
            parameters[2],
            parameters[3],
            relative,
            base,
            frame,
          );
          next = normalizedPoint(
            parameters[4],
            parameters[5],
            relative,
            base,
            frame,
          );
          rendered = `C ${sceneNumber(first.x)} ${sceneNumber(first.y)} ${sceneNumber(second.x)} ${sceneNumber(second.y)} ${sceneNumber(next.x)} ${sceneNumber(next.y)}`;
          if (!pointsAreFinite([first, second])) {
            return { error: "annotation path coordinate is outside the scene frame." };
          }
          break;
        }
        case "S":
        case "Q": {
          const control = normalizedPoint(
            parameters[0],
            parameters[1],
            relative,
            base,
            frame,
          );
          next = normalizedPoint(
            parameters[2],
            parameters[3],
            relative,
            base,
            frame,
          );
          rendered = `${emittedCommand} ${sceneNumber(control.x)} ${sceneNumber(control.y)} ${sceneNumber(next.x)} ${sceneNumber(next.y)}`;
          if (!pointsAreFinite([control])) {
            return { error: "annotation path coordinate is outside the scene frame." };
          }
          break;
        }
        case "A": {
          if (parameters[0] < 0 || parameters[1] < 0) {
            return { error: "annotation arc radii must be non-negative." };
          }
          const radiusX = normalizeNativeDistance(parameters[0], frame);
          const radiusY = normalizeNativeDistance(parameters[1], frame);
          next = normalizedPoint(
            parameters[5],
            parameters[6],
            relative,
            base,
            frame,
          );
          rendered = `A ${sceneNumber(radiusX)} ${sceneNumber(radiusY)} ${sceneNumber(parameters[2])} ${parameters[3]} ${parameters[4]} ${sceneNumber(next.x)} ${sceneNumber(next.y)}`;
          if (!Number.isFinite(radiusX) || !Number.isFinite(radiusY)) {
            return { error: "annotation arc radius is outside the scene frame." };
          }
          break;
        }
      }

      if (!pointsAreFinite([next])) {
        return { error: "annotation path coordinate is outside the scene frame." };
      }
      output.push(rendered);
      current = next;
      if (emittedCommand === "M") {
        subpathStart = next;
        hasMove = true;
      }
      groupIndex += 1;
      if (scanner.atEnd() || scanner.peekIsCommand()) {
        break;
      }
    }
  }

  return output.length === 0
    ? { error: "annotation path is empty." }
    : { d: output.join(" ") };
}
