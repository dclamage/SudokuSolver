import type {
  CandidateContext,
  CellShape,
  ConstraintInstance,
  EntityReference,
  JsonValue,
  PuzzlePackageV1,
} from "./types";

type UnknownRecord = Record<string, unknown>;

const entityKinds = ["cell", "group", "edge", "point", "path"] as const;
const requiredCandidateContextIds = [
  "setter-notes",
  "true-candidates",
  "logical-solver",
] as const;

function isRecord(value: unknown): value is UnknownRecord {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function expectRecord(value: unknown, description: string): UnknownRecord {
  if (!isRecord(value)) {
    throw new Error(`${description} must be an object`);
  }
  return value;
}

function expectArray(value: unknown, description: string): unknown[] {
  if (!Array.isArray(value)) {
    throw new Error(`${description} must be an array`);
  }
  return value;
}

function expectString(value: unknown, description: string): string {
  if (typeof value !== "string" || value.length === 0) {
    throw new Error(`${description} must be a non-empty string`);
  }
  return value;
}

function expectBoolean(value: unknown, description: string): boolean {
  if (typeof value !== "boolean") {
    throw new Error(`${description} must be a boolean`);
  }
  return value;
}

function expectFiniteNumber(value: unknown, description: string): number {
  if (typeof value !== "number" || !Number.isFinite(value)) {
    throw new Error(`${description} must be a finite number`);
  }
  return value;
}

function expectSafeInteger(value: unknown, description: string): number {
  if (typeof value !== "number" || !Number.isSafeInteger(value)) {
    throw new Error(`${description} must be a safe integer`);
  }
  return value;
}

function expectNonNegativeSafeInteger(
  value: unknown,
  description: string,
): number {
  if (
    typeof value !== "number" ||
    !Number.isSafeInteger(value) ||
    value < 0
  ) {
    throw new Error(`${description} must be a non-negative safe integer`);
  }
  return value;
}

function parseJsonValue(
  value: unknown,
  description: string,
  requireSafeIntegers: boolean,
): JsonValue {
  if (
    value === null ||
    typeof value === "boolean" ||
    typeof value === "string"
  ) {
    return value;
  }
  if (typeof value === "number") {
    if (requireSafeIntegers && !Number.isSafeInteger(value)) {
      throw new Error(`${description} must use finite safe integers`);
    }
    if (!Number.isFinite(value)) {
      throw new Error(`${description} must be valid JSON`);
    }
    return value;
  }
  if (Array.isArray(value)) {
    return value.map((item, index) =>
      parseJsonValue(item, `${description}[${index}]`, requireSafeIntegers),
    );
  }
  if (isRecord(value)) {
    const result: Record<string, JsonValue> = {};
    for (const [key, item] of Object.entries(value)) {
      try {
        result[key] = parseJsonValue(
          item,
          `${description}.${key}`,
          requireSafeIntegers,
        );
      } catch (error) {
        if (item === undefined) {
          throw new Error(`${description} must be valid JSON`);
        }
        throw error;
      }
    }
    return result;
  }
  throw new Error(`${description} must be valid JSON`);
}

function parseJsonRecord(
  value: unknown,
  description: string,
  requireSafeIntegers: boolean,
): Record<string, JsonValue> {
  const record = expectRecord(value, description);
  const result: Record<string, JsonValue> = {};
  for (const [key, item] of Object.entries(record)) {
    result[key] = parseJsonValue(
      item,
      `${description.slice(0, -1)} ${key}`,
      requireSafeIntegers,
    );
  }
  return result;
}

function parseStringArray(value: unknown, description: string): string[] {
  return expectArray(value, description).map((item, index) =>
    expectString(item, `${description}[${index}]`),
  );
}

function parseShape(value: unknown, cellId: string): CellShape {
  const shape = expectRecord(value, `cell ${cellId} shape`);
  if (shape.kind === "rect") {
    return {
      kind: "rect",
      x: expectFiniteNumber(shape.x, `cell ${cellId} shape x`),
      y: expectFiniteNumber(shape.y, `cell ${cellId} shape y`),
      width: expectFiniteNumber(shape.width, `cell ${cellId} shape width`),
      height: expectFiniteNumber(
        shape.height,
        `cell ${cellId} shape height`,
      ),
    };
  }
  if (shape.kind === "polygon") {
    return {
      kind: "polygon",
      points: expectArray(
        shape.points,
        `cell ${cellId} polygon points`,
      ).map((point, index) => {
        const pointRecord = expectRecord(
          point,
          `cell ${cellId} polygon point ${index}`,
        );
        return {
          x: expectFiniteNumber(
            pointRecord.x,
            `cell ${cellId} polygon point ${index} x`,
          ),
          y: expectFiniteNumber(
            pointRecord.y,
            `cell ${cellId} polygon point ${index} y`,
          ),
        };
      }),
    };
  }
  if (shape.kind === "path") {
    return {
      kind: "path",
      d: expectString(shape.d, `cell ${cellId} shape path`),
    };
  }
  throw new Error(`cell ${cellId} has invalid shape`);
}

function assertReferenceExists(
  kind: EntityReference["kind"],
  id: string,
  entities: Record<EntityReference["kind"], ReadonlySet<string>>,
  description: string,
): void {
  if (!entities[kind].has(id)) {
    throw new Error(`${description} references missing ${kind} ${id}`);
  }
}

function parseCandidateContext(
  value: unknown,
  index: number,
): CandidateContext {
  const context = expectRecord(value, `candidate context ${index}`);
  const id = expectString(context.id, `candidate context ${index} id`);
  const name = expectString(context.name, `candidate context ${id} name`);
  if (context.kind === "manual") {
    return { id, name, kind: "manual" };
  }
  if (context.kind === "trueCandidates") {
    if (context.refresh !== "automatic" && context.refresh !== "onRequest") {
      throw new Error(`candidate context ${id} has invalid refresh mode`);
    }
    if (
      context.display !== "possibility" &&
      context.display !== "solutionFrequency" &&
      context.display !== "logicComparison"
    ) {
      throw new Error(`candidate context ${id} has invalid display mode`);
    }
    return {
      id,
      name,
      kind: "trueCandidates",
      refresh: context.refresh,
      display: context.display,
      solutionCountCap: expectNonNegativeSafeInteger(
        context.solutionCountCap,
        `candidate context ${id} solutionCountCap`,
      ),
    };
  }
  if (context.kind === "logicalSolver") {
    if (context.followPuzzleRevision !== true) {
      throw new Error(
        `candidate context ${id} must follow the puzzle revision`,
      );
    }
    return {
      id,
      name,
      kind: "logicalSolver",
      followPuzzleRevision: true,
      enabledTechniqueIds: parseStringArray(
        context.enabledTechniqueIds,
        `candidate context ${id} enabledTechniqueIds`,
      ),
    };
  }
  throw new Error(`candidate context ${id} has invalid kind`);
}

export function validatePuzzlePackage(value: unknown): PuzzlePackageV1 {
  const root = expectRecord(value, "puzzle package");
  if (root.schemaVersion !== 1) {
    throw new Error("unsupported puzzle schema version");
  }

  const revision = expectNonNegativeSafeInteger(root.revision, "revision");
  const semanticRevision = expectNonNegativeSafeInteger(
    root.semanticRevision,
    "semanticRevision",
  );
  if (semanticRevision > revision) {
    throw new Error("semanticRevision cannot exceed revision");
  }

  const metadataValue = expectRecord(root.metadata, "metadata");
  const metadata = {
    title: expectString(metadataValue.title, "metadata title"),
    author:
      metadataValue.author === ""
        ? ""
        : expectString(metadataValue.author, "metadata author"),
    rules:
      metadataValue.rules === ""
        ? ""
        : expectString(metadataValue.rules, "metadata rules"),
  };

  const domainsValue = expectRecord(root.domains, "domains");
  const domains: PuzzlePackageV1["domains"] = {};
  for (const [domainKey, domainValue] of Object.entries(domainsValue)) {
    const domain = expectRecord(domainValue, `domain ${domainKey}`);
    const id = expectString(domain.id, `domain ${domainKey} id`);
    if (id !== domainKey) {
      throw new Error(`domain key ${domainKey} does not match id ${id}`);
    }
    const seenValueIds = new Set<string>();
    domains[domainKey] = {
      id,
      values: expectArray(domain.values, `domain ${id} values`).map(
        (item, index) => {
          const domainItem = expectRecord(
            item,
            `domain ${id} value ${index}`,
          );
          const valueId = expectString(
            domainItem.id,
            `domain ${id} value ${index} id`,
          );
          if (seenValueIds.has(valueId)) {
            throw new Error(`domain ${id} contains duplicate value ${valueId}`);
          }
          seenValueIds.add(valueId);
          return {
            id: valueId,
            label: expectString(
              domainItem.label,
              `domain ${id} value ${valueId} label`,
            ),
            ...(domainItem.numericValue === undefined
              ? {}
              : {
                  numericValue: expectSafeInteger(
                    domainItem.numericValue,
                    `domain ${id} value ${valueId} numericValue`,
                  ),
                }),
          };
        },
      ),
    };
  }

  const cellsValue = expectRecord(root.cells, "cells");
  const cells: PuzzlePackageV1["cells"] = {};
  for (const [cellKey, cellValue] of Object.entries(cellsValue)) {
    const cell = expectRecord(cellValue, `cell ${cellKey}`);
    const id = expectString(cell.id, `cell ${cellKey} id`);
    if (id !== cellKey) {
      throw new Error(`cell key ${cellKey} does not match id ${id}`);
    }
    const domainId = expectString(cell.domainId, `cell ${id} domainId`);
    if (!(domainId in domains)) {
      throw new Error(`cell ${id} references missing domain ${domainId}`);
    }
    const input = expectRecord(cell.input, `cell ${id} input`);
    cells[cellKey] = {
      id,
      domainId,
      shape: parseShape(cell.shape, id),
      input: {
        acceptsValue: expectBoolean(
          input.acceptsValue,
          `cell ${id} acceptsValue`,
        ),
        acceptsCandidates: expectBoolean(
          input.acceptsCandidates,
          `cell ${id} acceptsCandidates`,
        ),
      },
      ...(cell.label === undefined
        ? {}
        : { label: expectString(cell.label, `cell ${id} label`) }),
    };
  }

  const groupsValue = expectRecord(root.groups, "groups");
  const groups: PuzzlePackageV1["groups"] = {};
  for (const [groupKey, groupValue] of Object.entries(groupsValue)) {
    const group = expectRecord(groupValue, `group ${groupKey}`);
    const id = expectString(group.id, `group ${groupKey} id`);
    if (id !== groupKey) {
      throw new Error(`group key ${groupKey} does not match id ${id}`);
    }
    const cellIds = parseStringArray(group.cellIds, `group ${id} cellIds`);
    for (const cellId of cellIds) {
      if (!(cellId in cells)) {
        throw new Error(`group ${id} references missing cell ${cellId}`);
      }
    }
    groups[groupKey] = {
      id,
      roles: parseStringArray(group.roles, `group ${id} roles`),
      cellIds,
    };
  }

  const boardsValue = expectRecord(root.boards, "boards");
  const boards: PuzzlePackageV1["boards"] = {};
  for (const [boardKey, boardValue] of Object.entries(boardsValue)) {
    const board = expectRecord(boardValue, `board ${boardKey}`);
    const id = expectString(board.id, `board ${boardKey} id`);
    if (id !== boardKey) {
      throw new Error(`board key ${boardKey} does not match id ${id}`);
    }
    const cellIds = parseStringArray(board.cellIds, `board ${id} cellIds`);
    const groupIds = parseStringArray(board.groupIds, `board ${id} groupIds`);
    for (const cellId of cellIds) {
      if (!(cellId in cells)) {
        throw new Error(`board ${id} references missing cell ${cellId}`);
      }
    }
    for (const groupId of groupIds) {
      if (!(groupId in groups)) {
        throw new Error(`board ${id} references missing group ${groupId}`);
      }
    }
    boards[boardKey] = {
      id,
      name: expectString(board.name, `board ${id} name`),
      cellIds,
      groupIds,
    };
  }

  const adjacencyValue = expectRecord(root.adjacency, "adjacency");
  const adjacency: PuzzlePackageV1["adjacency"] = {};
  for (const [adjacencyKey, adjacencyItem] of Object.entries(adjacencyValue)) {
    const item = expectRecord(adjacencyItem, `adjacency ${adjacencyKey}`);
    const id = expectString(item.id, `adjacency ${adjacencyKey} id`);
    const fromCellId = expectString(item.fromCellId, `adjacency ${id} fromCellId`);
    const toCellId = expectString(item.toCellId, `adjacency ${id} toCellId`);
    if (!(fromCellId in cells)) {
      throw new Error(`adjacency ${id} references missing cell ${fromCellId}`);
    }
    if (!(toCellId in cells)) {
      throw new Error(`adjacency ${id} references missing cell ${toCellId}`);
    }
    adjacency[adjacencyKey] = {
      id,
      kind: expectString(item.kind, `adjacency ${id} kind`),
      fromCellId,
      toCellId,
    };
  }

  const pointsValue = expectRecord(root.points, "points");
  const points: PuzzlePackageV1["points"] = {};
  for (const [pointKey, pointValue] of Object.entries(pointsValue)) {
    const point = expectRecord(pointValue, `point ${pointKey}`);
    points[pointKey] = {
      id: expectString(point.id, `point ${pointKey} id`),
      x: expectFiniteNumber(point.x, `point ${pointKey} x`),
      y: expectFiniteNumber(point.y, `point ${pointKey} y`),
    };
  }

  const edgesValue = expectRecord(root.edges, "edges");
  const edges: PuzzlePackageV1["edges"] = {};
  for (const [edgeKey, edgeValue] of Object.entries(edgesValue)) {
    const edge = expectRecord(edgeValue, `edge ${edgeKey}`);
    const id = expectString(edge.id, `edge ${edgeKey} id`);
    const fromPointId = expectString(edge.fromPointId, `edge ${id} fromPointId`);
    const toPointId = expectString(edge.toPointId, `edge ${id} toPointId`);
    if (!(fromPointId in points)) {
      throw new Error(`edge ${id} references missing point ${fromPointId}`);
    }
    if (!(toPointId in points)) {
      throw new Error(`edge ${id} references missing point ${toPointId}`);
    }
    edges[edgeKey] = { id, fromPointId, toPointId };
  }

  const pathsValue = expectRecord(root.paths, "paths");
  const paths: PuzzlePackageV1["paths"] = {};
  for (const [pathKey, pathValue] of Object.entries(pathsValue)) {
    const path = expectRecord(pathValue, `path ${pathKey}`);
    const id = expectString(path.id, `path ${pathKey} id`);
    const pointIds = parseStringArray(path.pointIds, `path ${id} pointIds`);
    for (const pointId of pointIds) {
      if (!(pointId in points)) {
        throw new Error(`path ${id} references missing point ${pointId}`);
      }
    }
    paths[pathKey] = {
      id,
      pointIds,
      closed: expectBoolean(path.closed, `path ${id} closed`),
    };
  }

  const entities: Record<EntityReference["kind"], ReadonlySet<string>> = {
    cell: new Set(Object.keys(cells)),
    group: new Set(Object.keys(groups)),
    edge: new Set(Object.keys(edges)),
    point: new Set(Object.keys(points)),
    path: new Set(Object.keys(paths)),
  };

  const constraints: ConstraintInstance[] = expectArray(
    root.constraints,
    "constraints",
  ).map((constraintValue, index) => {
    const constraint = expectRecord(constraintValue, `constraint at index ${index}`);
    const id = expectString(constraint.id, `constraint ${index} id`);
    const bindingsValue = expectRecord(
      constraint.bindings,
      `constraint ${id} bindings`,
    );
    const bindings: Record<string, EntityReference[]> = {};
    for (const [role, bindingValue] of Object.entries(bindingsValue)) {
      if (!Array.isArray(bindingValue)) {
        throw new Error(`constraint ${id} binding ${role} must be an array`);
      }
      bindings[role] = bindingValue.map((referenceValue, referenceIndex) => {
        const reference = expectRecord(
          referenceValue,
          `constraint ${id} binding ${role} reference ${referenceIndex}`,
        );
        if (
          typeof reference.kind !== "string" ||
          !entityKinds.includes(reference.kind as (typeof entityKinds)[number])
        ) {
          throw new Error(
            `constraint ${id} binding ${role} has invalid entity kind`,
          );
        }
        const kind = reference.kind as EntityReference["kind"];
        const referenceId = expectString(
          reference.id,
          `constraint ${id} binding ${role} reference id`,
        );
        assertReferenceExists(kind, referenceId, entities, `constraint ${id}`);
        return { kind, id: referenceId };
      });
    }
    return {
      id,
      typeId: expectString(constraint.typeId, `constraint ${id} typeId`),
      ...(constraint.definitionReleaseId === undefined
        ? {}
        : {
            definitionReleaseId: expectString(
              constraint.definitionReleaseId,
              `constraint ${id} definitionReleaseId`,
            ),
          }),
      bindings,
      parameters: parseJsonRecord(
        constraint.parameters,
        `constraint ${id} parameters`,
        true,
      ),
      ...(constraint.styleOverrides === undefined
        ? {}
        : {
            styleOverrides: parseJsonRecord(
              constraint.styleOverrides,
              `constraint ${id} styleOverrides`,
              false,
            ),
          }),
    };
  });

  const givensValue = expectRecord(root.givens, "givens");
  const givens: PuzzlePackageV1["givens"] = {};
  for (const [cellId, givenValue] of Object.entries(givensValue)) {
    if (!(cellId in cells)) {
      throw new Error(`given references missing cell ${cellId}`);
    }
    const valueId = expectString(givenValue, `given ${cellId} value`);
    const domain = domains[cells[cellId].domainId];
    if (!domain.values.some((domainValue) => domainValue.id === valueId)) {
      throw new Error(`given ${cellId} references missing value ${valueId}`);
    }
    givens[cellId] = valueId;
  }

  const solverProjections = expectArray(
    root.solverProjections,
    "solverProjections",
  ).map((projectionValue, projectionIndex) => {
    const projection = expectRecord(
      projectionValue,
      `projection at index ${projectionIndex}`,
    );
    const id = expectString(projection.id, `projection ${projectionIndex} id`);
    if (projection.kind !== "latin-square") {
      throw new Error(`projection ${id} has unsupported kind`);
    }
    const boardId = expectString(projection.boardId, `projection ${id} boardId`);
    if (!(boardId in boards)) {
      throw new Error(`projection ${id} references missing board ${boardId}`);
    }
    const domainId = expectString(
      projection.domainId,
      `projection ${id} domainId`,
    );
    if (!(domainId in domains)) {
      throw new Error(`projection ${id} references missing domain ${domainId}`);
    }
    const rowValues = expectArray(
      projection.cellIdsByRow,
      `projection ${id} cellIdsByRow`,
    );
    const cellIdsByRow = rowValues.map((row, rowIndex) =>
      parseStringArray(row, `projection ${id} row ${rowIndex}`),
    );
    const firstRowLength = cellIdsByRow[0]?.length ?? 0;
    if (cellIdsByRow.some((row) => row.length !== firstRowLength)) {
      throw new Error(`projection ${id} rows must have equal lengths`);
    }
    if (cellIdsByRow.length === 0 || firstRowLength !== cellIdsByRow.length) {
      throw new Error(`projection ${id} must be square`);
    }
    const projectedCellIds = new Set<string>();
    const boardCellIds = new Set(boards[boardId].cellIds);
    for (const cellId of cellIdsByRow.flat()) {
      if (projectedCellIds.has(cellId)) {
        throw new Error(`projection contains duplicate cell ${cellId}`);
      }
      if (!(cellId in cells)) {
        throw new Error(`projection ${id} references missing cell ${cellId}`);
      }
      if (!boardCellIds.has(cellId)) {
        throw new Error(`projection ${id} cell ${cellId} is not on its board`);
      }
      if (cells[cellId].domainId !== domainId) {
        throw new Error(
          `projection ${id} cell ${cellId} uses a different domain`,
        );
      }
      projectedCellIds.add(cellId);
    }
    const domain = domains[domainId];
    if (domain.values.length !== cellIdsByRow.length) {
      throw new Error(`projection ${id} domain size must match grid size`);
    }
    const valueIdsBySolverValue = parseStringArray(
      projection.valueIdsBySolverValue,
      `projection ${id} valueIdsBySolverValue`,
    );
    const domainValueIds = new Set(domain.values.map((item) => item.id));
    if (
      valueIdsBySolverValue.length !== domain.values.length ||
      new Set(valueIdsBySolverValue).size !== domain.values.length ||
      valueIdsBySolverValue.some((valueId) => !domainValueIds.has(valueId))
    ) {
      throw new Error(
        `projection ${id} value order must cover its domain exactly`,
      );
    }
    valueIdsBySolverValue.forEach((valueId, index) => {
      const domainValue = domain.values.find((item) => item.id === valueId);
      if (domainValue?.numericValue !== index + 1) {
        throw new Error(
          `projection ${id} value ${valueId} must have numeric interpretation ${index + 1}`,
        );
      }
    });
    return {
      id,
      kind: "latin-square" as const,
      boardId,
      domainId,
      valueIdsBySolverValue,
      cellIdsByRow,
    };
  });

  const presentationValue = expectRecord(root.presentation, "presentation");
  const presentation = {
    styles: parseJsonRecord(
      presentationValue.styles,
      "presentation styles",
      false,
    ),
    sceneElements: expectArray(
      presentationValue.sceneElements,
      "presentation sceneElements",
    ).map((item, index) =>
      parseJsonValue(item, `presentation sceneElement ${index}`, false),
    ),
  };

  const authoringValue = expectRecord(root.authoring, "authoring");
  const candidateContexts = expectArray(
    authoringValue.candidateContexts,
    "authoring candidateContexts",
  ).map(parseCandidateContext);
  const contextIds = new Set<string>();
  for (const context of candidateContexts) {
    if (contextIds.has(context.id)) {
      throw new Error(`duplicate candidate context ${context.id}`);
    }
    contextIds.add(context.id);
  }
  for (const contextId of requiredCandidateContextIds) {
    if (!contextIds.has(contextId)) {
      throw new Error(`missing candidate context ${contextId}`);
    }
  }
  const manualMarksValue = expectRecord(
    authoringValue.manualMarks,
    "authoring manualMarks",
  );
  const manualMarks: PuzzlePackageV1["authoring"]["manualMarks"] = {};
  for (const [contextId, marksValue] of Object.entries(manualMarksValue)) {
    if (!contextIds.has(contextId)) {
      throw new Error(`manual marks reference missing context ${contextId}`);
    }
    const marks = expectRecord(marksValue, `manual marks ${contextId}`);
    manualMarks[contextId] = {};
    for (const [cellId, values] of Object.entries(marks)) {
      if (!(cellId in cells)) {
        throw new Error(`manual marks reference missing cell ${cellId}`);
      }
      const valueIds = parseStringArray(
        values,
        `manual marks ${contextId} cell ${cellId}`,
      );
      const domainValueIds = new Set(
        domains[cells[cellId].domainId].values.map((item) => item.id),
      );
      for (const valueId of valueIds) {
        if (!domainValueIds.has(valueId)) {
          throw new Error(
            `manual marks ${contextId} cell ${cellId} references missing value ${valueId}`,
          );
        }
      }
      manualMarks[contextId][cellId] = valueIds;
    }
  }

  const extensionsValue = expectRecord(root.extensions, "extensions");
  const extensions: Record<
    string,
    { impact: "semantic" | "cosmetic"; data: JsonValue }
  > = {};
  for (const [extensionId, extensionValue] of Object.entries(extensionsValue)) {
    const extension = expectRecord(extensionValue, `extension ${extensionId}`);
    if (extension.impact !== "semantic" && extension.impact !== "cosmetic") {
      throw new Error(`extension ${extensionId} has invalid impact`);
    }
    extensions[extensionId] = {
      impact: extension.impact,
      data: parseJsonValue(
        extension.data,
        `extension ${extensionId} data`,
        extension.impact === "semantic",
      ),
    };
  }

  return {
    schemaVersion: 1,
    id: expectString(root.id, "puzzle id"),
    revision,
    semanticRevision,
    metadata,
    domains,
    cells,
    boards,
    groups,
    adjacency,
    points,
    edges,
    paths,
    givens,
    constraints,
    solverProjections,
    presentation,
    ...(root.source === undefined
      ? {}
      : { source: parseJsonValue(root.source, "source", false) }),
    ...(root.release === undefined
      ? {}
      : { release: parseJsonValue(root.release, "release", true) }),
    ...(root.assets === undefined
      ? {}
      : {
          assets: expectArray(root.assets, "assets").map((asset, index) =>
            parseJsonValue(asset, `asset ${index}`, false),
          ),
        }),
    ...(root.provenance === undefined
      ? {}
      : { provenance: parseJsonValue(root.provenance, "provenance", false) }),
    authoring: { candidateContexts, manualMarks },
    extensions,
  };
}
