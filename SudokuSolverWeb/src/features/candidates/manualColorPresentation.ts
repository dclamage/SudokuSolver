import type {
  CellId,
  ManualColorToken,
} from "../../domain/puzzle/types";
import type { SceneCellFill } from "../../scene/types";

export interface ManualColorOption {
  readonly id: ManualColorToken;
  readonly value: string;
}

export const manualColorOptions: readonly ManualColorOption[] = Object.freeze([
  Object.freeze({ id: "cyan", value: "#39c6f4" }),
  Object.freeze({ id: "green", value: "#4bd37b" }),
  Object.freeze({ id: "yellow", value: "#f2c94c" }),
  Object.freeze({ id: "rose", value: "#ee6c8a" }),
]);

const fillByToken: Readonly<Record<ManualColorToken, SceneCellFill>> =
  Object.freeze(
    Object.fromEntries(
      manualColorOptions.map((option) => [
        option.id,
        Object.freeze({ color: option.value, label: option.id }),
      ]),
    ) as Record<ManualColorToken, SceneCellFill>,
  );

export function presentManualCellColors(
  colors: Readonly<Record<CellId, ManualColorToken>> | undefined,
): Readonly<Record<CellId, SceneCellFill>> {
  if (colors === undefined) {
    return Object.freeze({});
  }
  return Object.freeze(
    Object.fromEntries(
      Object.entries(colors).map(([cellId, token]) => [
        cellId,
        fillByToken[token],
      ]),
    ),
  );
}
