# AI Agent Contribution Guidelines (AGENTS.MD)

## 1. Introduction
    - **Purpose**: This document guides AI agents in contributing to the SudokuSolver codebase, ensuring consistency, quality, and maintainability.
    - **Philosophy**: AI contributions should augment human developers by implementing new features, optimizing performance, fixing bugs, and assisting with documentation, all while adhering to the established coding standards and architectural patterns.

## 2. Core Principles
    - **Clarity and Readability**: Code must be easily understandable by human developers. Prioritize clear logic over overly complex or "clever" solutions.
    - **Maintainability**: Contributions should be structured to simplify future modifications, debugging, and extensions.
    - **Correctness**: All implemented logic must be functionally correct, robust, and handle edge cases appropriately.
    - **Consistency**: Adhere strictly to existing coding styles, naming conventions, architectural choices, and patterns found within the codebase.
    - **Efficiency**: Be mindful of performance, especially in core solver logic and frequently called methods. However, balance performance with clarity, particularly for user-facing logical steps.
    - **Modularity and Reusability**: Design new components (especially utility functions) with reusability in mind. If a generic solution can benefit multiple parts of the codebase, prefer it over a highly specific one. Conversely, use existing utility functions whenever available.

## 3. Development Environment & Style
    - **C# Version**: 12.0
    - **.NET Target**: .NET 8
    - **Code Formatting**: Strictly adhere to the formatting guidelines defined in the `.editorconfig` file located in the project's base directory.
    - **Naming Conventions**: Follow standard .NET naming conventions (e.g., PascalCase for methods, properties, and classes; camelCase for local variables and private fields).
    - **File Organization**: New files should be placed in appropriate directories (e.g., new constraints under `SudokuSolver\Constraints\`, new strategies under `SudokuSolver\Constraints\Strategies\`).

## 4. Code Generation & Modification
    - **Scope of Changes**:
        - For standard tasks, modify existing methods/classes with minimal changes to achieve the goal.
        - When implementing significant new functionality or if refactoring is explicitly requested, create new methods/classes that follow existing organizational patterns.
        - Identify and note code smells or areas for improvement, but do not refactor beyond the immediate task scope unless instructed.
    - **Comments and Documentation**:
        - **XML Documentation**: Update existing XML documentation comments (`<summary>`, `<param>`, `<returns>`, etc.) to accurately reflect any changes made. New public and internal members **must** have comprehensive XML documentation.
        - **Inline Comments**: Use inline comments sparingly. They should only clarify complex, non-obvious logic or important algorithmic choices. Avoid comments that merely restate what the code does.
    - **Error Handling**:
        - Use standard .NET exception types (e.g., `ArgumentException`, `InvalidOperationException`, `NotImplementedException`) as appropriate.
        - Ensure error messages are clear, concise, and informative, helping to pinpoint the source of the error.
    - **LINQ Usage**:
        - Utilize LINQ for data manipulation and querying where it enhances readability and conciseness, consistent with its usage in existing code.
        - Be mindful of LINQ's performance implications in hot paths; prefer imperative code if LINQ introduces significant overhead in critical sections.
    - **Async/Await**: Use `async` and `await` for I/O-bound operations (e.g., in `WebsocketListener.cs`) or genuinely long-running CPU-bound tasks that can benefit from non-blocking execution (e.g., some brute-force operations if they were to involve external calls, though current brute-force is CPU-intensive and uses `Task.Run` for parallelism).
    - **Immutability**: Favor immutability for data structures where practical, especially for objects passed between different parts of the system or used in multi-threaded contexts.
    - **Performance Considerations**:
        - **Critical Sections**: `SolverLogic.cs` (especially `FindHiddenSingle`, `FindNakedSingles`), `SolverBruteForce.cs`, and the `StepLogic` / `EnforceConstraint` methods of all `Constraint` classes are performance-sensitive.
        - **Brute-Force Logic**: In `Constraint.StepLogic` when `isBruteForcing` is true, and in methods within `SolverBruteForceLogic.cs` (e.g., `FastAdvancedStrategies`), prioritize raw speed. Logic here should significantly reduce the search space to be worthwhile.
        - **Logical Solving Steps**: For `Constraint.StepLogic` when `isBruteForcing` is false, prioritize clarity of the logical step to the user and comprehensiveness in applying human-like solving techniques. Performance is still important but secondary to logical rigor and descriptive output.
        - **Avoid Unnecessary Allocations**: Be mindful of object allocations in loops or frequently called methods. Use `structs` for small, short-lived data containers where appropriate (see `AICSolver.StrongLinkDesc`).

## 5. Working with Existing Code
    - **Understanding Context**: Before modifying any code, make an effort to understand its purpose, its interactions with other components (e.g., how `Solver` interacts with `Constraint` instances), and the rationale behind existing design choices.
    - **Refactoring**:
        - Address `TODO` comments or obvious code smells only if they directly impede the current task or if explicitly instructed. Otherwise, note them for human review.
        - Major refactoring initiatives require explicit prompting.
    - **Solver Internals**:
        - Interact with solver internals (e.g., `board`, `weakLinks`, `_candidateCountsPerGroupValue`) primarily through the `Solver` class's public or internal methods (`SetValue`, `ClearValue`, `AddWeakLink`, `KeepMask`, etc.).
        - Direct manipulation should be rare and well-justified.
    - **Constraint System**:
        - **`Constraint` Base Class**: New constraints must inherit from `Constraint` and correctly implement its abstract and virtual methods.
        - **`InitCandidates`**: Use to perform initial candidate eliminations based on the constraint's static properties before any cells are set.
        - **`EnforceConstraint`**: This method is called frequently (every time a cell value is set). It **must be fast**.
            - Focus on direct, simple rule validation.
            - It can perform trivial eliminations that are fundamental to the constraint and don't require explanation to the user.
            - Logic already covered by standard group uniqueness (handled by the solver) or by weak links (established in `InitLinks`) should not be re-checked here.
        - **`StepLogic(Solver sudokuSolver, List<LogicalStepDesc> logicalStepDescription, bool isBruteForcing)`**:
            - If `isBruteForcing` is `true`: Implement only logic that provides a significant speed-up to the brute-force search by pruning the search tree effectively. Avoid complex deductions.
            - If `isBruteForcing` is `false`: Implement logic that a human solver would use. Generate clear, informative `LogicalStepDesc` objects detailing the technique, the affected candidates/cells, and the eliminations. Use `Solver.DescribeElims` for formatting elimination lists.
        - **`InitLinks(Solver solver, List<LogicalStepDesc> logicalStepDescription, bool isInitializing)`**:
            - This is called once during solver initialization (`isInitializing = true`) and is periodically called again during logical (non-brute force) solves.
            - **Prefer using weak links** (`solver.AddWeakLink(candA, candB)`) to define relationships where `candidateA implies NOT candidateB`. This is a cornerstone of the `AICSolver`.
            - Example: `WhispersConstraint` adds weak links between candidates in adjacent cells if their values violate the whisper difference.
        - **`SplitToPrimitives`**: Implement if the constraint can be broken down into simpler, fundamental components (e.g., a long line constraint into pairwise constraints). This is used by the solver for `IsInheritOf` logic.
        - **`CellsMustContain`**: Implement if the constraint can determine that a specific value *must* be placed in one of a small subset of its cells.
        - **Helper Classes**: Utilize `SumGroup` and `SumCellsHelper` for constraints involving sums over a set of cells (e.g., Killer Cages, Arrows).
    - **`SolverFactory.cs`**: When adding support for new f-puzzles constraint types, ensure parsing logic is robust and new constraints are correctly instantiated and added to the `Solver`.
    - **`AICSolver.cs`**: This class relies heavily on the weak and strong links established by constraints and basic Sudoku rules. Changes here require a deep understanding of alternating inference chains.

## 6. Testing
    - **Unit Tests**:
        - Create NUnit unit tests for any new utility functions or complex, isolated algorithms. Place these in the `SudokuTests` project.
        - Follow existing testing patterns (e.g., `TestUtility.cs` for helper methods).
    - **Integration Tests (Puzzle-Based)**:
        - New constraint types or significant changes to existing constraint logic are primarily tested by adding valid puzzles that utilize the feature to `SudokuTests\Puzzles.cs` or specific test files (e.g., `ArrowTests.cs`). These puzzles will be provided in the prompt.
        - Ensure that the solver can correctly solve these puzzles and that logical steps (if applicable) are reported as expected.

## 7. What to Avoid
    - Introducing breaking changes to public APIs of `Solver` or widely used utility classes without explicit instruction.
    - Adding new third-party dependencies.
    - Removing or significantly altering existing comments that explain critical design decisions or complex business logic without strong justification and explicit instruction.
    - Implementing overly complex or obscure code ("clever code") that sacrifices readability and maintainability for marginal or unproven performance gains.
    - Making large, unfocused changes across multiple files. Keep changes targeted to the task at hand.
    - Directly manipulating UI elements or engaging in concerns outside the `SudokuSolver` library and `SudokuSolverConsole`'s core logic (e.g., do not attempt to modify Visual Studio settings directly).

## 8. Clarification and Ambiguity
    - If requirements in a prompt are unclear, ambiguous, or seem to conflict with these guidelines, state any assumptions made or explicitly ask for clarification.
    - If multiple valid implementation approaches exist, choose the one that is most performant while being consistent with existing patterns and maintaining clarity. If a trade-off is significant, you may note the alternatives.

## 9. Commit Message Format (Simulated)
    - **Line 1**: Short summary (e.g., "Implement SandwichConstraint StepLogic").
    - **Line 2**: Empty.
    - **Line 3 onwards**:
        - Use major headings (e.g., "### Added", "### Changed", "### Fixed").
        - Under each heading, use bullet points to describe specific changes.

    Example:
    ```
    Implement PillArrowStrategy EnforceConstraint

    ### Changed
    - Updated `EnforceConstraint` in `PillArrowStrategy.cs` to correctly validate pill and arrow sums when a cell value is set.
    - Added checks for scenarios where either the pill or arrow is fully valued, or when neither is fully valued but their possible sums do not overlap.
    ```