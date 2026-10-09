-- Generated from src/host/Refusals.fs by scripts/generate-lua.fsx. Do not edit by hand.
local M = {
    codes = {
        loopBody = { block_mark_title = "⊘ Cannot run: inside a loop", title = "Cannot run: inside a loop", detail = "FsHttp.Studio cannot run a request inside a loop. A loop body describes many requests, and one Run sends one request. To run this request, bind it to a name outside the loop, then run that binding." },
        ifBranch = { block_mark_title = "⊘ Cannot run: inside an if branch", title = "Cannot run: inside an if branch", detail = "FsHttp.Studio cannot run a request inside an if branch. The script chooses the branch when it runs, so FsHttp.Studio cannot tell which request you want. To run this request, bind it to a name outside the if, then run that binding." },
        matchClause = { block_mark_title = "⊘ Cannot run: inside a match clause", title = "Cannot run: inside a match clause", detail = "FsHttp.Studio cannot run a request inside a match clause. The script chooses the clause when it runs, so FsHttp.Studio cannot tell which request you want. To run this request, bind it to a name outside the match, then run that binding." },
        exceptionHandler = { block_mark_title = "⊘ Cannot run: inside a try block", title = "Cannot run: inside a try block", detail = "FsHttp.Studio cannot run a request inside a try block. The script chooses the handler when it runs. To run this request, bind it to a name outside the try, then run that binding." },
        needsArguments = { block_mark_title = "⊘ Cannot run: this function needs arguments", title = "Cannot run: this function needs arguments", detail = "FsHttp.Studio cannot run a request in a function that takes arguments, because it has no values to supply. To run this request, move it to a binding that takes no arguments." },
        classMember = { block_mark_title = "⊘ Cannot run: inside a class member", title = "Cannot run: inside a class member", detail = "FsHttp.Studio cannot run a request in a class member, because it has no instance of the class. To run this request, move it to a module-level binding." },
        innerBinding = { block_mark_title = "⊘ Cannot run: inside a local binding", title = "Cannot run: inside a local binding", detail = "FsHttp.Studio cannot run a request in a local binding. A local binding is not in scope after the script runs. To run this request, move it to a module-level binding." },
        lambdaValue = { block_mark_title = "⊘ Cannot run: this binding is a function", title = "Cannot run: this binding is a function", detail = "This binding is a function rather than a request. FsHttp.Studio sends the request only when your code calls the function. To run this request, bind it directly to a name." },
        noNameToCall = { block_mark_title = "⊘ Cannot run: this binding has no name", title = "Cannot run: this binding has no name", detail = "The pattern of this binding gives FsHttp.Studio no name to call. To run this request, bind it to a simple name." },
        tupleBinding = { block_mark_title = "⊘ Cannot run: this binding binds two or more values", title = "Cannot run: this binding binds two or more values", detail = "This binding binds two or more values, so its value is not the request alone. To run this request, give it its own let binding." },
        insideAnotherRequest = { block_mark_title = "⊘ Cannot run: inside another request", title = "Cannot run: inside another request", detail = "This request is inside another request. FsHttp.Studio can run the outer request only. To run this request, move it to its own binding." },
        unaddressable = { block_mark_title = "⊘ Cannot run in this position", title = "Cannot run in this position", detail = "FsHttp.Studio cannot address a request in this position. To run this request, move it to its own let binding, at the top level of the script or of a module." },
    },
    fallback_code = "unaddressable",
    run_block_mark_title = "▶ Run request",
    stale_block_index = { title = "Cannot run: the script changed", detail = "This request moved or was removed after you started the Run. FsHttp.Studio cannot find it at the position it had when the Run started. To run this request, run :FsHttp run again." },
    unbound_block_value = { title = "Cannot run: depends on another request", detail = "This request uses `{name}`, which another request in this script binds. One Run evaluates one request, so `{name}` has no value. FsHttp.Studio cannot run a request that depends on another request." },
    companion_stopped = { title = "Cannot run: the companion stopped", detail = "The FsHttp.Studio companion stopped. Reload the window to start it again." },
    companion_stopped_block_mark_title = "⊘ Cannot run: the companion stopped",
    no_blocks_parse_failure = "No requests found: this script has a syntax error.",
    no_blocks_parse_failure_block_mark_title = "⊘ No requests found: this script has a syntax error",
    no_blocks_empty = "This script has no request. Write an http { } block to run one.",
}

---@param code string
---@return { block_mark_title: string, title: string, detail: string }
function M.entry(code)
    return M.codes[code] or M.codes[M.fallback_code]
end

return M
