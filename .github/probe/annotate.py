# TEMPORARY (T03 probe, removed before review): writes failed tests from a `dotnet test` log as annotations.
import re, sys
log = open(sys.argv[1], encoding="utf-8", errors="replace").read().splitlines()
blocks, i = [], 0
while i < len(log):
    m = re.match(r"^\s*failed (\S+)", log[i])
    if m:
        body = []
        j = i + 1
        while j < len(log) and len(body) < 12 and "Stack Trace" not in log[j] and not re.match(r"^\s*(failed|passed) ", log[j]):
            if log[j].strip():
                body.append(log[j].strip())
            j += 1
        blocks.append((m.group(1), body))
        i = j
    else:
        i += 1
summary = [l.strip() for l in log if re.search(r"(Test run summary|total:|failed:|succeeded:|skipped:)", l)]
print("::notice title=Test summary::" + "%0A".join(summary[-12:])[:3000])
kinds = ["error"] * 10 + ["warning"] * 10 + ["notice"] * 9
for kind, (name, body) in zip(kinds, blocks):
    text = ("%0A".join(body))[:3000].replace("\r", "")
    print(f"::{kind} title={name[-120:]}::{text}")
print(f"::notice title=Failed tests::{len(blocks)}")
