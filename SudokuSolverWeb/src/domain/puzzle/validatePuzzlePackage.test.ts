import fixture from "../../../../test-fixtures/native/classic-with-auxiliary.json";
import killerFixture from "../../../../test-fixtures/native/four-by-four-killer.json";
import semanticHashes from "../../../../test-fixtures/native/semantic-hashes.json";
import { describe, expect, it } from "vitest";

import { canonicalJson } from "./canonicalJson";
import { computeSemanticHash } from "./computeSemanticHash";
import { createStarterPuzzle } from "./createStarterPuzzle";
import type { PuzzlePackageV1 } from "./types";
import { validatePuzzlePackage } from "./validatePuzzlePackage";

describe("validatePuzzlePackage", () => {
  it("accepts an explicit Latin projection with an ignored auxiliary cell", () => {
    const puzzle = validatePuzzlePackage(fixture);

    expect(puzzle.solverProjections[0].cellIdsByRow).toHaveLength(9);
    expect(puzzle.cells["aux-1"].domainId).toBe("digits-1-9");
    expect(puzzle.solverProjections[0].cellIdsByRow.flat()).not.toContain("aux-1");
  });

  it("rejects duplicate projected cell IDs", () => {
    const invalid = structuredClone(fixture);
    invalid.solverProjections[0].cellIdsByRow[0][1] = "r1c1";

    expect(() => validatePuzzlePackage(invalid)).toThrow(
      "projection contains duplicate cell r1c1",
    );
  });

  it("rejects unsupported schema versions", () => {
    const invalid = structuredClone(fixture) as unknown as {
      schemaVersion: number;
    };
    invalid.schemaVersion = 2;

    expect(() => validatePuzzlePackage(invalid)).toThrow(
      "unsupported puzzle schema version",
    );
  });

  it("rejects cells whose domain is missing", () => {
    const invalid = structuredClone(fixture);
    invalid.cells.r1c1.domainId = "missing";

    expect(() => validatePuzzlePackage(invalid)).toThrow(
      "cell r1c1 references missing domain missing",
    );
  });

  it("rejects topology references to missing entities", () => {
    const invalid = structuredClone(fixture);
    invalid.boards.main.groupIds[0] = "missing";

    expect(() => validatePuzzlePackage(invalid)).toThrow(
      "board main references missing group missing",
    );
  });

  it.each([
    {
      entityType: "adjacency",
      mutate: (invalid: PuzzlePackageV1) => {
        invalid.adjacency["adjacency-key"] = {
          id: "adjacency-id",
          kind: "orthogonal",
          fromCellId: "r1c1",
          toCellId: "r1c2",
        };
      },
    },
    {
      entityType: "point",
      mutate: (invalid: PuzzlePackageV1) => {
        invalid.points["point-key"] = { id: "point-id", x: 0, y: 0 };
      },
    },
    {
      entityType: "edge",
      mutate: (invalid: PuzzlePackageV1) => {
        invalid.points.from = { id: "from", x: 0, y: 0 };
        invalid.points.to = { id: "to", x: 1, y: 0 };
        invalid.edges["edge-key"] = {
          id: "edge-id",
          fromPointId: "from",
          toPointId: "to",
        };
      },
    },
    {
      entityType: "path",
      mutate: (invalid: PuzzlePackageV1) => {
        invalid.points.anchor = { id: "anchor", x: 0, y: 0 };
        invalid.paths["path-key"] = {
          id: "path-id",
          pointIds: ["anchor"],
          closed: false,
        };
      },
    },
  ])(
    "rejects a $entityType record whose key differs from its embedded ID",
    ({ entityType, mutate }) => {
      const invalid = structuredClone(fixture) as unknown as PuzzlePackageV1;
      mutate(invalid);

      expect(() => validatePuzzlePackage(invalid)).toThrow(
        `${entityType} key ${entityType}-key does not match id ${entityType}-id`,
      );
    },
  );

  it("rejects constraint bindings that are not entity-reference arrays", () => {
    const invalid = structuredClone(fixture) as unknown as PuzzlePackageV1;
    invalid.constraints = [
      {
        id: "broken",
        typeId: "example.constraint",
        bindings: { cells: "r1c1" as unknown as readonly [] },
        parameters: {},
      },
    ];

    expect(() => validatePuzzlePackage(invalid)).toThrow(
      "constraint broken binding cells must be an array",
    );
  });

  it("rejects constraint bindings to missing entities", () => {
    const invalid = structuredClone(fixture) as unknown as PuzzlePackageV1;
    invalid.constraints = [
      {
        id: "broken",
        typeId: "example.constraint",
        bindings: { cells: [{ kind: "cell", id: "missing" }] },
        parameters: {},
      },
    ];

    expect(() => validatePuzzlePackage(invalid)).toThrow(
      "constraint broken references missing cell missing",
    );
  });

  it("rejects malformed constraint parameters", () => {
    const invalid = structuredClone(fixture) as unknown as PuzzlePackageV1;
    invalid.constraints = [
      {
        id: "broken",
        typeId: "example.constraint",
        bindings: {},
        parameters: { amount: undefined as unknown as number },
      },
    ];

    expect(() => validatePuzzlePackage(invalid)).toThrow(
      "constraint broken parameter amount must be valid JSON",
    );
  });

  it("rejects non-square projections", () => {
    const invalid = structuredClone(fixture);
    invalid.solverProjections[0].cellIdsByRow.pop();

    expect(() => validatePuzzlePackage(invalid)).toThrow(
      "projection main-latin-square must be square",
    );
  });

  it("rejects projection rows of different lengths", () => {
    const invalid = structuredClone(fixture);
    invalid.solverProjections[0].cellIdsByRow[0].pop();

    expect(() => validatePuzzlePackage(invalid)).toThrow(
      "projection main-latin-square rows must have equal lengths",
    );
  });

  it("rejects a projection whose domain size differs from its grid size", () => {
    const invalid = structuredClone(fixture);
    invalid.domains["digits-1-9"].values.pop();

    expect(() => validatePuzzlePackage(invalid)).toThrow(
      "projection main-latin-square domain size must match grid size",
    );
  });

  it("rejects projection value orders that do not cover the domain", () => {
    const invalid = structuredClone(fixture);
    invalid.solverProjections[0].valueIdsBySolverValue[8] = "8";

    expect(() => validatePuzzlePackage(invalid)).toThrow(
      "projection main-latin-square value order must cover its domain exactly",
    );
  });

  it("rejects projection values without matching numeric interpretations", () => {
    const invalid = structuredClone(fixture);
    invalid.domains["digits-1-9"].values[0].numericValue = 9;

    expect(() => validatePuzzlePackage(invalid)).toThrow(
      "projection main-latin-square value 1 must have numeric interpretation 1",
    );
  });

  it("rejects invalid givens", () => {
    const invalid = structuredClone(fixture) as unknown as PuzzlePackageV1;
    invalid.givens = { r1c1: "missing" };

    expect(() => validatePuzzlePackage(invalid)).toThrow(
      "given r1c1 references missing value missing",
    );
  });

  it("rejects negative and decreasing revisions", () => {
    const negative = structuredClone(fixture);
    negative.revision = -1;
    expect(() => validatePuzzlePackage(negative)).toThrow(
      "revision must be a non-negative safe integer",
    );

    const decreasing = structuredClone(fixture);
    decreasing.revision = 1;
    decreasing.semanticRevision = 2;
    expect(() => validatePuzzlePackage(decreasing)).toThrow(
      "semanticRevision cannot exceed revision",
    );
  });

  it("rejects missing default candidate context IDs", () => {
    const invalid = structuredClone(fixture);
    invalid.authoring.candidateContexts = invalid.authoring.candidateContexts.filter(
      (context) => context.id !== "logical-solver",
    );

    expect(() => validatePuzzlePackage(invalid)).toThrow(
      "missing candidate context logical-solver",
    );
  });

  it("preserves validated forward-compatible sections without sharing input references", () => {
    const candidate = structuredClone(fixture) as unknown as PuzzlePackageV1;
    const source = { format: "example", version: 1 };
    candidate.source = source;
    candidate.release = { releases: { "release-1": { version: 1 } } };
    candidate.assets = [{ id: "asset-1", mediaType: "image/svg+xml" }];
    candidate.provenance = { importedBy: "contract-test" };
    candidate.extensions = {
      "example:semantic": { impact: "semantic", data: { enabled: true } },
      "example:cosmetic": { impact: "cosmetic", data: { opacity: 1 } },
    };

    const puzzle = validatePuzzlePackage(candidate);
    source.format = "mutated";

    expect(puzzle.source).toEqual({ format: "example", version: 1 });
    expect(puzzle.release).toEqual({
      releases: { "release-1": { version: 1 } },
    });
    expect(puzzle.assets).toEqual([
      { id: "asset-1", mediaType: "image/svg+xml" },
    ]);
    expect(puzzle.provenance).toEqual({ importedBy: "contract-test" });
    expect(puzzle.extensions).toEqual({
      "example:semantic": { impact: "semantic", data: { enabled: true } },
      "example:cosmetic": { impact: "cosmetic", data: { opacity: 1 } },
    });
  });

  it("rejects malformed extension impact declarations", () => {
    const invalid = structuredClone(fixture) as unknown as PuzzlePackageV1;
    invalid.extensions = {
      "example:unknown": {
        impact: "unknown" as "semantic",
        data: { enabled: true },
      },
    };

    expect(() => validatePuzzlePackage(invalid)).toThrow(
      "extension example:unknown has invalid impact",
    );
  });

  it.each(["unnamespaced", ":name", "namespace:", "namespace:   "])(
    "rejects extension ID %s without non-empty namespace and name tokens",
    (extensionId) => {
      const invalid = structuredClone(fixture) as unknown as PuzzlePackageV1;
      invalid.extensions = {
        [extensionId]: {
          impact: "semantic",
          data: { enabled: true },
        },
      };

      expect(() => validatePuzzlePackage(invalid)).toThrow(
        `extension ${extensionId} must use <namespace>:<name>`,
      );
    },
  );

  it("rejects unsafe semantic numbers while permitting decimal geometry", () => {
    const invalid = structuredClone(fixture) as unknown as PuzzlePackageV1;
    invalid.constraints = [
      {
        id: "unsafe",
        typeId: "example.constraint",
        bindings: {},
        parameters: { amount: 1.5 },
      },
    ];
    expect(() => validatePuzzlePackage(invalid)).toThrow(
      "constraint unsafe parameter amount must use finite safe integers",
    );

    const decimalGeometry = structuredClone(fixture);
    decimalGeometry.cells["aux-1"].shape.x = 10.5;
    expect(() => validatePuzzlePackage(decimalGeometry)).not.toThrow();
  });
});

describe("canonicalJson", () => {
  it("recursively sorts object keys while retaining array order", () => {
    expect(canonicalJson({ z: 1, a: [{ z: 3, y: 2 }, 4] })).toBe(
      '{"a":[{"y":2,"z":3},4],"z":1}',
    );
  });
});

describe("computeSemanticHash", () => {
  it("matches the hand-recorded shared fixture goldens", async () => {
    await expect(
      computeSemanticHash(validatePuzzlePackage(fixture)),
    ).resolves.toBe(semanticHashes["classic-with-auxiliary"]);
    await expect(
      computeSemanticHash(validatePuzzlePackage(killerFixture)),
    ).resolves.toBe(semanticHashes["four-by-four-killer"]);
  });

  it("matches the shared Unicode semantic-string golden", async () => {
    const unicode = validatePuzzlePackage(fixture);
    unicode.extensions = {
      "example:unicode": {
        impact: "semantic",
        data: {
          emoji: "😀",
          lineSeparator: "before after",
        },
      },
    };

    await expect(computeSemanticHash(unicode)).resolves.toBe(
      semanticHashes["classic-with-unicode-semantic-extension"],
    );
  });

  it("excludes auxiliary-cell geometry", async () => {
    const original = validatePuzzlePackage(fixture);
    const moved = structuredClone(original);
    const auxiliaryShape = moved.cells["aux-1"].shape;
    if (auxiliaryShape.kind !== "rect") {
      throw new Error("fixture auxiliary cell must be rectangular");
    }
    auxiliaryShape.x = 24.5;

    await expect(computeSemanticHash(moved)).resolves.toBe(
      await computeSemanticHash(original),
    );
  });

  it("includes givens", async () => {
    const original = validatePuzzlePackage(fixture);
    const changed = structuredClone(original);
    changed.givens.r1c1 = "5";

    await expect(computeSemanticHash(changed)).resolves.not.toBe(
      await computeSemanticHash(original),
    );
  });

  it("includes the domain of an auxiliary cell outside the solver projection", async () => {
    const original = validatePuzzlePackage(fixture);
    original.domains["alternate-digits"] = {
      id: "alternate-digits",
      values: original.domains["digits-1-9"].values.map((value) => ({
        ...value,
      })),
    };
    const changed = structuredClone(original);
    changed.cells["aux-1"].domainId = "alternate-digits";

    await expect(computeSemanticHash(changed)).resolves.not.toBe(
      await computeSemanticHash(original),
    );
  });

  it("includes typed killer parameters", async () => {
    const original = validatePuzzlePackage(killerFixture);
    const changed = structuredClone(original);
    changed.constraints[0].parameters = { sum: 4 };

    await expect(computeSemanticHash(changed)).resolves.not.toBe(
      await computeSemanticHash(original),
    );
  });

  it("includes the entire opaque release payload when a release is referenced", async () => {
    const original = validatePuzzlePackage(fixture);
    original.constraints = [
      {
        id: "custom-1",
        typeId: "example.custom",
        definitionReleaseId: "release-1",
        bindings: {},
        parameters: {},
      },
    ];
    original.release = {
      releases: {
        "release-1": { artifactHash: "sha256:artifact" },
      },
      compilerVersion: 1,
    };
    const changed = structuredClone(original);
    changed.release = {
      releases: {
        "release-1": { artifactHash: "sha256:artifact" },
      },
      compilerVersion: 2,
    };

    await expect(computeSemanticHash(changed)).resolves.not.toBe(
      await computeSemanticHash(original),
    );
  });

  it("includes semantic extensions and excludes cosmetic extensions", async () => {
    const original = validatePuzzlePackage(fixture);
    const semantic = structuredClone(original);
    semantic.extensions = {
      "example:semantic": {
        impact: "semantic",
        data: { enabled: true },
      },
    };
    const cosmetic = structuredClone(original);
    cosmetic.extensions = {
      "example:cosmetic": {
        impact: "cosmetic",
        data: { opacity: 0.5 },
      },
    };

    await expect(computeSemanticHash(semantic)).resolves.not.toBe(
      await computeSemanticHash(original),
    );
    await expect(computeSemanticHash(cosmetic)).resolves.toBe(
      await computeSemanticHash(original),
    );
  });
});

describe("createStarterPuzzle", () => {
  it("creates a validated independent starter with a caller-provided ID", () => {
    const starter = createStarterPuzzle(() => "starter-id");

    expect(starter.id).toBe("starter-id");
    expect(starter.metadata.title).toBe("Untitled puzzle");
    expect(starter.cells["aux-1"].id).toBe("aux-1");
    expect(starter).not.toBe(fixture);
  });
});
