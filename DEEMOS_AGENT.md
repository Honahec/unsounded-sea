# Hyper3D Agent

Hyper3D Agent is a conversational workspace for creating and refining 3D assets, working with reference files, and inspecting generated results.
This document contains the complete browser usage guide for the Agent workspace.
Agent workspace entry: [https://hyper3d.ai/workspace/agent](https://hyper3d.ai/workspace/agent)
These instructions describe the browser workflow. They do not define a public API or MCP interface. Use the signed-in workspace and the controls actually visible in the current session. Labels may be translated or customized by the host website.
Hyper3D Agent accepts natural-language requests and reference files, shows the agent's messages and tool progress, and presents generated assets for preview and download. Projects organize chats and assets; a thread is a chat within a project; a run is the execution of a submitted request. An artifact is an uploaded or generated file.
Use a browser interaction tool for the application. A text-only page fetch can read this guide but cannot operate the interactive workspace. This guide does not specify direct API requests.

1. Open the Agent workspace. If login is required, let the user complete the site's normal sign-in flow and then resume in their session.
2. For a follow-up, open the existing project and chat so their context and assets are available. For a separate task, use New chat and check the destination project shown above the composer. Use New project when the task needs its own project.
3. The sidebar filter switches between Active and Archived. Archived projects are read-only; opening one does not restore it.
4. Inspect the composer, attached files, and selected skills. A new chat has no fixed sample prompt, but it can restore an unsent draft. The text beginning “Describe your 3D goal” is a placeholder. Resolve any unrelated draft before submitting the current request; do not silently send it or discard the user's unrelated work.
   Use project and thread links produced by the site; do not construct URLs from guessed IDs or undocumented query parameters.
5. Describe the desired asset, intended use, style, and important constraints. Specify a requested output format or geometry target when relevant; availability depends on the workflow and returned outputs.
6. To add references, use Add files or project assets and Upload from device, or the available drag-and-drop control. To reuse an existing asset, choose it in the asset picker or use the editor's @ reference picker. A local filename typed into a prompt does not attach the file.
7. Wait for attachments to finish uploading. Resolve failed uploads before sending; pending or failed uploads can block submission.
8. Use the optional skills selector when a displayed skill fits the request. Review restored skill selections as well as the text.
9. Enter the prompt through the editable textbox. Check the resulting text and inline references before submission. Prefer accessible names and current page state over CSS class names or fixed screen coordinates.
10. Click Send once. A host landing-page composer may instead use Generate. Check that the message or run appears before attempting another submission.

Example request:
Create a stylized low-poly camping lantern for a browser game using the attached image as the shape reference. Use painted metal and warm glass, keep the handle separate from the body, and provide a textured GLB if available. Show the result so I can inspect its silhouette and materials.
This is an example request, not a guarantee that every requested constraint or format can be fulfilled.
Messages, tool activity, and artifacts can arrive at different times. Observe the existing run until it completes, fails, is cancelled, or requests a decision. An early thumbnail or an intermediate assistant message does not by itself mean the final asset is ready.
If the agent asks for a decision, read the question and options and respond within the user's existing instructions. Ask the user when a necessary choice is unresolved. Do not treat a pending decision as a stalled run.
The composer action changes with state: Stop cancels the current run, while Queue message adds a follow-up during a run. Repeated clicks do not refresh progress. Check the current action label before using it.
If the connection drops, return to the same chat and inspect its latest state before resubmitting. Report a visible failure and its message accurately; use an offered retry only when appropriate for the user's task. Avoid creating duplicate runs to check progress.
Open the resulting asset in the preview area. Check that it belongs to the requested chat or run, especially when the asset list includes project-wide results. For a model, inspect its shape and materials in the 3D viewer; for other outputs, use the provided preview.
Use the asset's Download action and available formats. Some outputs download as a bundle. Verify that the download completed before reporting a saved file, and return the actual local path when your tools provide it. A preview image is not the model file.
For revisions, continue in the same chat and reference the specific asset and desired changes. In the final response, describe the delivered output, link the actual result or chat when available, and mention any unmet requirements or remaining decisions. A chat URL is not necessarily a public share link; use the site's sharing flow only when sharing is part of the user's request.
