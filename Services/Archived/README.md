# Archived: vulkaninfo checker

`VulkanInfoCheckerService.cs.archived` is the previous implementation of the DXVK
capability check. It shelled out to `vulkaninfo.exe` once per GPU and parsed the
generated JSON.

It is kept for historical/reference purposes only:

- the `.cs.archived` extension means it is **not compiled**;
- the live check is `Services/VulkanCheckerService.cs`;
- the raw hardware probing is `Services/Vulkan/VulkanProbeService.cs` (headless,
  in-process, no external executable).

The archived file is still the best description of the original support tiers:

- tier 3: latest DXVK (all required extensions/limits)
- tier 2: DXVK 2.6.2 (robustness2 + transform feedback)
- tier 1: legacy DXVK 1.x (Vulkan 1.2 without the DXVK 2 requirements)
- tier 0: unsupported

Do not re-enable it. If the embedded probe ever needs to be replaced, keep the
same aggregate rules in `VulkanCheckerService`.
