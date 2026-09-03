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

  private skipWhitespace() {
    while (this.index < this.source.length && /[\s]/.test(this.source[this.index])) {
      this.index += 1;
    }
  }

  private nextNonWhitespaceIndex() {
    let index = this.index;
    while (index < this.source.length && /[\s]/.test(this.source[index])) {
      index += 1;
    }
    return index;
  }

  public atEnd() {
    return this.nextNonWhitespaceIndex() >= this.source.length;
  }

  public peekIsCommand() {
    return /[A-Za-z]/.test(
      this.source[this.nextNonWhitespaceIndex()] ?? "",
    );
  }

  public readCommand() {
    this.skipWhitespace();
    const command = this.source[this.index];
    if (command === undefined || !/[A-Za-z]/.test(command)) {
      return undefined;
    }
    this.index += 1;
    return command;
  }

  private prepareNumber(
    separator: "after-command" | "arc-compact" | "between-numbers",
  ) {
    const start = this.index;
    this.skipWhitespace();
    const hadWhitespace = this.index > start;
    if (separator === "after-command") {
      return this.source[this.index] === "," ? false : true;
    }
    if (this.source[this.index] === ",") {
      this.index += 1;
      this.skipWhitespace();
      return this.source[this.index] !== "," && this.index < this.source.length;
    }
    if (separator === "arc-compact" || hadWhitespace) {
      return true;
    }
    const next = this.source[this.index];
    return next === "+" || next === "-";
  }

  public readNumber(
    separator: "after-command" | "arc-compact" | "between-numbers",
  ) {
    if (!this.prepareNumber(separator)) {
      return undefined;
    }
    const match = this.source.slice(this.index).match(NUMBER_TOKEN);
    if (match === null) {
      return undefined;
    }
    this.index += match[0].length;
    const value = Number(match[0]);
    return Number.isFinite(value) ? value : undefined;
  }

  public readFlag(
    separator: "after-command" | "arc-compact" | "between-numbers",
  ) {
    if (!this.prepareNumber(separator)) {
      return undefined;
    }
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

function readParameters(
  scanner: PathScanner,
  command: Exclude<PathCommand, "Z">,
  firstGroup: boolean,
) {
  const parameters: number[] = [];
  for (let index = 0; index < PARAMETER_COUNTS[command]; index += 1) {
    const separator =
      firstGroup && index === 0
        ? "after-command"
        : command === "A" && (index === 4 || index === 5)
          ? "arc-compact"
          : "between-numbers";
    const value =
      command === "A" && (index === 3 || index === 4)
        ? scanner.readFlag(separator)
        : scanner.readNumber(separator);
    if (value === undefined) {
      return undefined;
    }
    parameters.push(value);
  }
  return parameters;
}

function nativePoint(
  x: number,
  y: number,
  relative: boolean,
  current: NormalizedPoint,
) {
  return relative
    ? {
        x: current.x + x,
        y: current.y + y,
      }
    : { x, y };
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
  let currentNative: NormalizedPoint = { x: 0, y: 0 };
  let subpathStartNative = currentNative;
  let lastCubicControlNative: NormalizedPoint | undefined;
  let lastQuadraticControlNative: NormalizedPoint | undefined;
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
      currentNative = subpathStartNative;
      lastCubicControlNative = undefined;
      lastQuadraticControlNative = undefined;
      activeCommand = undefined;
      continue;
    }

    let groupIndex = 0;
    while (true) {
      const parameters = readParameters(scanner, command, groupIndex === 0);
      if (parameters === undefined) {
        return { error: `annotation path has invalid ${command} parameters.` };
      }
      const emittedCommand = command === "M" && groupIndex > 0 ? "L" : command;
      const baseNative = currentNative;
      let nextNative = currentNative;
      let rendered: string;

      switch (emittedCommand) {
        case "M":
        case "L": {
          nextNative = nativePoint(
            parameters[0],
            parameters[1],
            relative,
            baseNative,
          );
          const next = normalizeNativePoint(nextNative, frame);
          rendered = `${emittedCommand} ${sceneNumber(next.x)} ${sceneNumber(next.y)}`;
          lastCubicControlNative = undefined;
          lastQuadraticControlNative = undefined;
          break;
        }
        case "H": {
          nextNative = {
            x: relative ? baseNative.x + parameters[0] : parameters[0],
            y: baseNative.y,
          };
          const next = normalizeNativePoint(nextNative, frame);
          rendered = `H ${sceneNumber(next.x)}`;
          lastCubicControlNative = undefined;
          lastQuadraticControlNative = undefined;
          break;
        }
        case "V": {
          nextNative = {
            x: baseNative.x,
            y: relative ? baseNative.y + parameters[0] : parameters[0],
          };
          const next = normalizeNativePoint(nextNative, frame);
          rendered = `V ${sceneNumber(next.y)}`;
          lastCubicControlNative = undefined;
          lastQuadraticControlNative = undefined;
          break;
        }
        case "C": {
          const firstNative = nativePoint(
            parameters[0],
            parameters[1],
            relative,
            baseNative,
          );
          const secondNative = nativePoint(
            parameters[2],
            parameters[3],
            relative,
            baseNative,
          );
          nextNative = nativePoint(
            parameters[4],
            parameters[5],
            relative,
            baseNative,
          );
          const first = normalizeNativePoint(firstNative, frame);
          const second = normalizeNativePoint(secondNative, frame);
          const next = normalizeNativePoint(nextNative, frame);
          rendered = `C ${sceneNumber(first.x)} ${sceneNumber(first.y)} ${sceneNumber(second.x)} ${sceneNumber(second.y)} ${sceneNumber(next.x)} ${sceneNumber(next.y)}`;
          if (!pointsAreFinite([firstNative, secondNative, first, second])) {
            return { error: "annotation path coordinate is outside the scene frame." };
          }
          lastCubicControlNative = secondNative;
          lastQuadraticControlNative = undefined;
          break;
        }
        case "S": {
          const reflectedControlNative =
            lastCubicControlNative === undefined
              ? baseNative
              : {
                  x: baseNative.x + (baseNative.x - lastCubicControlNative.x),
                  y: baseNative.y + (baseNative.y - lastCubicControlNative.y),
                };
          const controlNative = nativePoint(
            parameters[0],
            parameters[1],
            relative,
            baseNative,
          );
          nextNative = nativePoint(
            parameters[2],
            parameters[3],
            relative,
            baseNative,
          );
          const control = normalizeNativePoint(controlNative, frame);
          const next = normalizeNativePoint(nextNative, frame);
          rendered = `S ${sceneNumber(control.x)} ${sceneNumber(control.y)} ${sceneNumber(next.x)} ${sceneNumber(next.y)}`;
          if (!pointsAreFinite([reflectedControlNative, controlNative, control])) {
            return { error: "annotation path coordinate is outside the scene frame." };
          }
          lastCubicControlNative = controlNative;
          lastQuadraticControlNative = undefined;
          break;
        }
        case "Q": {
          const controlNative = nativePoint(
            parameters[0],
            parameters[1],
            relative,
            baseNative,
          );
          nextNative = nativePoint(
            parameters[2],
            parameters[3],
            relative,
            baseNative,
          );
          const control = normalizeNativePoint(controlNative, frame);
          const next = normalizeNativePoint(nextNative, frame);
          rendered = `Q ${sceneNumber(control.x)} ${sceneNumber(control.y)} ${sceneNumber(next.x)} ${sceneNumber(next.y)}`;
          if (!pointsAreFinite([controlNative, control])) {
            return { error: "annotation path coordinate is outside the scene frame." };
          }
          lastCubicControlNative = undefined;
          lastQuadraticControlNative = controlNative;
          break;
        }
        case "T": {
          const reflectedControlNative =
            lastQuadraticControlNative === undefined
              ? baseNative
              : {
                  x:
                    baseNative.x +
                    (baseNative.x - lastQuadraticControlNative.x),
                  y:
                    baseNative.y +
                    (baseNative.y - lastQuadraticControlNative.y),
                };
          nextNative = nativePoint(
            parameters[0],
            parameters[1],
            relative,
            baseNative,
          );
          const next = normalizeNativePoint(nextNative, frame);
          rendered = `T ${sceneNumber(next.x)} ${sceneNumber(next.y)}`;
          if (!pointsAreFinite([reflectedControlNative])) {
            return { error: "annotation path coordinate is outside the scene frame." };
          }
          lastCubicControlNative = undefined;
          lastQuadraticControlNative = reflectedControlNative;
          break;
        }
        case "A": {
          if (parameters[0] < 0 || parameters[1] < 0) {
            return { error: "annotation arc radii must be non-negative." };
          }
          const radiusX = normalizeNativeDistance(parameters[0], frame);
          const radiusY = normalizeNativeDistance(parameters[1], frame);
          nextNative = nativePoint(
            parameters[5],
            parameters[6],
            relative,
            baseNative,
          );
          const next = normalizeNativePoint(nextNative, frame);
          rendered = `A ${sceneNumber(radiusX)} ${sceneNumber(radiusY)} ${sceneNumber(parameters[2])} ${parameters[3]} ${parameters[4]} ${sceneNumber(next.x)} ${sceneNumber(next.y)}`;
          if (!Number.isFinite(radiusX) || !Number.isFinite(radiusY)) {
            return { error: "annotation arc radius is outside the scene frame." };
          }
          lastCubicControlNative = undefined;
          lastQuadraticControlNative = undefined;
          break;
        }
      }

      const next = normalizeNativePoint(nextNative, frame);
      if (!pointsAreFinite([nextNative, next])) {
        return { error: "annotation path coordinate is outside the scene frame." };
      }
      output.push(rendered);
      currentNative = nextNative;
      if (emittedCommand === "M") {
        subpathStartNative = nextNative;
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
