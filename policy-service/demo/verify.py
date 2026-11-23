"""Verify the live blocked-to-mergeable PR demo using authenticated gh."""
import argparse
import json
import subprocess

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("repository")
parser.add_argument("pull_request", type=int)
parser.add_argument("blocked_sha")
parser.add_argument("--capture-blocked", action="store_true")
parser.add_argument("app_slug")
args = parser.parse_args()

def gh(*arguments):
    return json.loads(subprocess.check_output(["gh", *arguments], text=True))

def check(sha, conclusion, expected_text):
    runs = gh("api", "--paginate", "--slurp",
              f"repos/{args.repository}/commits/{sha}/check-runs?per_page=100")
    matches = [
        run for page in runs for run in page["check_runs"]
        if run["name"] == "ForgeScope / merge policy"
        and run["app"]["slug"] == args.app_slug
        and run["external_id"] == f"{args.repository}#{args.pull_request}"
    ]
    if len(matches) != 1:
        raise SystemExit(f"{sha}: expected exactly one ForgeScope check, found {len(matches)}")
    run = matches[0]
    if run["conclusion"] != conclusion or expected_text not in run["output"]["summary"]:
        raise SystemExit(f"{sha}: expected {conclusion} with evidence: {expected_text}")
    return {"sha": sha, "conclusion": conclusion,
            "check": f"https://github.com/{args.repository}/pull/{args.pull_request}/checks?check_run_id={run['id']}"}

pr = gh("pr", "view", str(args.pull_request), "--repo", args.repository,
        "--json", "headRefOid,mergeStateStatus,mergeable,isDraft")
head = pr["headRefOid"]
if args.capture_blocked:
    if head != args.blocked_sha or pr["isDraft"] or pr["mergeStateStatus"] != "BLOCKED":
        raise SystemExit("Expected this head on an open, non-draft PR blocked from merge")
    blocked = check(head, "failure", "is missing unexpired artifact")
    print(json.dumps({"blocked": blocked, "merge_state": pr["mergeStateStatus"]}, indent=2))
    raise SystemExit(0)

if head == args.blocked_sha or pr["isDraft"] or pr["mergeStateStatus"] != "CLEAN" or pr["mergeable"] != "MERGEABLE":
    raise SystemExit("Expected an updated, non-draft PR with a clean merge state")
blocked = check(args.blocked_sha, "failure", "is missing unexpired artifact")
passing = check(head, "success", "meets the CI requirements")
print(json.dumps({"blocked": blocked, "passing": passing, "current_merge_state": pr["mergeStateStatus"]}, indent=2))
