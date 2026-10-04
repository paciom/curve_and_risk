// The instruction the loop gives the coding agent for one file. Pure: no I/O.
//
// The prompt states the rules, but it is not what enforces them: loop.mjs checks every one of them
// after the agent finishes, and an attempt that breaks a rule is reverted whatever the agent says.

const describe = mutant =>
  `- line ${mutant.line}, column ${mutant.column}: \`${mutant.original}\` replaced by \`${mutant.replacement}\` (${mutant.mutator})`;

const RULES = `Rules, checked by the script after you finish:
- Change files under tests/ only. A change anywhere else is reverted and the attempt fails.
- The whole test suite must pass on the unmodified code.
- \`node tools/check.mjs --fast\` must pass. Do not suppress a rule.
- A mutant counts only when Stryker reports it killed. Saying it is killed counts for nothing.
- Do not run Stryker yourself; the script does.

Test the behaviour the mutation breaks, through the public surface, in the style of the neighbouring
tests. If no test could observe a mutant, write no test for it and say so in your final message, one
line per mutant: "UNOBSERVABLE line:column reason". A person reviews those claims.`;

export function buildPrompt(file, targets, feedback) {
  const sections = [
    "You are one step of an automated mutation-testing loop. A script reads your result, not a person.",
    `Stryker made the changes below to ${file} and no test failed. Write or strengthen tests so that each change makes a test fail.`,
    targets.map(describe).join("\n"),
    RULES,
  ];
  if (feedback) {
    sections.push(`Your previous attempt on this file was checked. Result:\n${feedback}`);
  }
  return sections.join("\n\n");
}
