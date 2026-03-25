#!/bin/bash
# MonoDebug End-to-End Test Suite
# Usage: ./run_all.sh <sdb_port> [monodebug_path]

PORT=${1:?Usage: $0 <sdb_port> [monodebug_path]}
MONO=${2:-monodebug}
PASS=0
FAIL=0
TOTAL=0
TMPOUT=$(mktemp)

run() { "$MONO" "$@" > "$TMPOUT" 2>&1 || true; }
check() {
   TOTAL=$((TOTAL + 1))
   local name="$1"; local expect="$2"
   if grep -q "$expect" "$TMPOUT" 2>/dev/null; then
      PASS=$((PASS + 1)); echo "  ✓ $name"
   else
      FAIL=$((FAIL + 1)); echo "  ✗ $name"
      echo "    expected: $expect"
      echo "    got: $(cat $TMPOUT)"
   fi
}

echo "======== A. ERROR CASES ========"
run; check "A1 no args" '"success":false'
run status; check "A2 no daemon" 'CONNECT_FAILED'
run attach; check "A3 attach no port" 'INVALID_ARGS'

echo "======== B. SESSION ========"
run attach "$PORT"; check "B1 attach" '"success":true'
run status; check "B2 status" '"connected":true'
run status --full; check "B3 status --full" '"threads"'

echo "======== C. BREAK ========"
run break set DebugTest.cs 14; check "C1 set" '"success":true'
run break set DebugTest.cs 14; check "C2 duplicate BP" '"success":true'
run break set DebugTest.cs 19 --temp; check "C3 set --temp" '"isTemp":true'
run break DebugTest.cs 14; check "C4 no set" 'Unknown break command'
run break set; check "C5 no args" 'INVALID_ARGS'
run break list; check "C6 list" '"breakpoints":'
run break disable 4; check "C7 disable" 'Disabled'
run break enable 4; check "C8 enable" 'Enabled'
run break remove 3; check "C9 remove dup" 'Removed'
run break remove 4; check "C10 remove temp" 'Removed'
run break remove 999; check "C11 not found" 'not found'
run break remove --all; check "C12 remove all" 'Removed'
run break set DebugTest.cs 14; check "C13 re-set" '"success":true'

echo "======== D. CATCH + ISOLATION ========"
run catch set NullReferenceException; check "D1 set" '"success":true'
run catch NullReferenceException; check "D2 no set" 'Unknown catch command'
run catch list; check "D3 list" '"catchpoints":'
run catch remove --all; check "D4 remove --all" 'Removed'
run break list; check "D5 BP survived" '"breakpoints":'

echo "======== E. FLOW + BP HIT ========"
run flow wait --timeout 5000; check "E1 vmstart" '"reason":"vmstart"'
run flow continue; check "E2 continue" 'Resumed'
run flow wait --timeout 15000; check "E3 BP hit" '"reason":"breakpoint"'

echo "======== F. INSPECT + EVAL ========"
run vars; check "F1 vars" '"this":'
run vars --depth 2; check "F2 vars --depth" '"this":'
run vars --static DebugTest; check "F3 vars --static" '"type":"DebugTest"'
run stack; check "F4 stack" '"frames":'
run stack --full; check "F5 stack --full" '"this":'
run eval 'this.speed'; check "F6 eval field" '"success":true'
run eval '1 + 2'; check "F8 eval arithmetic" '"value":"3"'
run eval 'this.speed * 2'; check "F9 eval mixed" '"value":'
run eval 'this.label.Length'; check "F10 eval property" '"value":'
run eval 'counter > 100'; check "F11 eval comparison" '"value":'
run thread list; check "F12 thread list" '"threads":'
# vars set after eval
run vars set speed 10.0; check "F13 set speed" 'Set speed'
run vars set counter 999; check "F14 set counter" 'Set counter'
run vars set label Test; check "F15 set label" 'Set label'
run vars set nonexist 1; check "F16 set nonexist" 'EVAL_ERROR'
run vars set; check "F17 set no args" 'INVALID_ARGS'

echo "======== G. STEPPING ========"
run flow step; run flow wait --timeout 5000; check "G1 step into" '"reason":'
run flow next; run flow wait --timeout 5000; check "G2 next" '"reason":'
run flow out; run flow wait --timeout 5000; check "G3 out" '"reason":'
run flow continue; run flow wait --timeout 5000; check "G4 continue+hit" '"reason":'

echo "======== G2. STACK FRAME ========"
run flow step; run flow wait --timeout 5000
run stack frame 1; check "G5 frame 1" '"frame":1'
run stack frame 0; check "G6 frame 0" '"frame":0'

echo "======== G3. FLOW UNTIL + GOTO ========"
run flow out; run flow wait --timeout 5000
run flow continue; run flow wait --timeout 5000
run flow until DebugTest.cs 19; check "G7 until" 'Running to'
run flow wait --timeout 5000; check "G8 until hit" '"line":'
# goto within same method (ProcessFrame: 19 → 21)
run flow goto DebugTest.cs 21; check "G9 goto" 'Set IP'
run stack; check "G10 goto verify" '"line":21'

echo "======== G4. STEP COUNT ========"
run flow continue; run flow wait --timeout 5000
run flow next --count 2; check "G11 next --count" 'Step next'
run flow wait --timeout 5000; check "G12 count result" '"reason":'

echo "======== H. PAUSE ========"
run flow continue
sleep 1
run flow pause; check "H1 pause" 'Suspended'
run vars; check "H2 vars after pause" '"this":'
run eval 'this.counter'; check "H3 eval after pause" '"success":true'
# continue when already running
# Running state tests — remove all BPs first so VM actually runs
run break remove --all; check "H4 remove all BPs" 'Removed'
run flow continue
sleep 1
run vars; check "H5 vars while running" 'NOT_STOPPED'
run eval 'this.speed'; check "H6 eval while running" 'NOT_STOPPED'
run flow step; check "H7 step while running" 'NOT_STOPPED'
# Re-set BP for remaining tests
run break set DebugTest.cs 14; check "H8 BP set while running" '"success":true'
run flow pause; check "H9 re-pause" 'Suspended'

echo "======== I. PROFILE ========"
run flow pause
run profile list; check "I1 list" '"profiles":'
run profile create test --desc "T"; check "I2 create" '"success":true'
run profile create default; check "I3 create dup" 'ALREADY_EXISTS'
run profile info test; check "I4 info" '"profile":'
run profile info; check "I5 info no args" 'INVALID_ARGS'
run profile edit test --rename test-v2; check "I6 edit rename" 'test-v2'
run profile enable nonexist; check "I7 enable nonexist" 'NOT_FOUND'
run profile switch test-v2; check "I8 switch" 'Switched'
run profile disable --all; check "I9 disable --all" 'Disabled all'
run profile enable default; check "I10 enable" 'Enabled'
run profile remove test-v2; check "I11 remove" 'Removed'
run profile remove default; check "I12 remove default" 'Cannot remove'
run profile foo; check "I13 unknown" 'Unknown profile command'

echo "======== J. ERRORS ========"
run flow xyz; check "J1 unknown flow" 'Unknown flow command'
run foobar; check "J2 unknown group" 'Unknown command group'
run eval; check "J3 eval no expr" 'INVALID_ARGS'

echo "======== L. DETACH ========"
run detach; check "L1 detach" 'Detached'
sleep 3
TOTAL=$((TOTAL + 1))
if tasklist //FI "IMAGENAME eq monodebug.exe" 2>&1 | grep -q monodebug; then
   FAIL=$((FAIL + 1)); echo "  ✗ L2 daemon exit"
else
   PASS=$((PASS + 1)); echo "  ✓ L2 daemon exit"
fi

rm -f "$TMPOUT"

echo ""
echo "======== RESULTS ========"
echo "  Total: $TOTAL"
echo "  Pass:  $PASS"
echo "  Fail:  $FAIL"
[ "$FAIL" -gt 0 ] && exit 1 || exit 0
