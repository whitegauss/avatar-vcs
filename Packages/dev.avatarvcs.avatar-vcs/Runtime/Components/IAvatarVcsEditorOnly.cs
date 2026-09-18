#if AVATARVCS_VRCSDK3_AVATARS
using VRC.SDKBase;
#endif

namespace AvatarVcs.Runtime
{
    /// <summary>
    /// Marks this tool's own components as editor-side only. With the VRChat
    /// avatar SDK installed it derives from VRC.SDKBase.IEditorOnly; without
    /// it, it is an empty interface and nothing else changes -- the same shape
    /// NDMF uses for INDMFEditorOnly, so the package still compiles in a
    /// project that has no VRChat SDK at all.
    ///
    /// Why this matters enough to take a dependency on the SDK (AvatarVcsRoot
    /// used to say, deliberately, that it stayed clear of every SDK type):
    /// the SDK's avatar validation is a whitelist of type *names*
    /// (VRC.SDKBase.Validation.AvatarValidation.ComponentTypeWhiteListCommon),
    /// so every component in this package counts as illegal. The build tab
    /// reports that as an error, which blocks the build, and offers an "Auto
    /// Fix" that runs Undo.DestroyObjectImmediate over the lot. Destroying
    /// AvatarVcsRoot takes its avatarGuid with it, and that guid is the only
    /// key the avatar's stored commit history has -- a beta project lost four
    /// histories that way. AvatarValidation.FindIllegalComponents is called
    /// with excludeEditorOnly: true, so implementing this keeps our markers
    /// out of that list entirely, which is how MA's components manage not to
    /// appear there.
    ///
    /// The other side of the bargain: the SDK destroys IEditorOnly components
    /// during build preprocessing, which is what we want (none of this belongs
    /// in an uploaded avatar) as long as it happens on a copy. NDMF clones the
    /// avatar before that runs, and MA replaces the SDK's own callback to push
    /// it late for the same reason. In a project with neither, an upload may
    /// take the markers off the scene avatar -- no worse than today, where the
    /// build stops until the user destroys them by hand, and the history that
    /// used to be lost with them is now only a rename away (KAN-85).
    /// </summary>
    public interface IAvatarVcsEditorOnly
#if AVATARVCS_VRCSDK3_AVATARS
        : IEditorOnly
#endif
    {
    }
}
