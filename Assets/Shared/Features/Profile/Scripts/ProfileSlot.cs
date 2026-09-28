namespace Shared.Profile
{
    /// <summary>
    /// Which of the two pickable slots an item belongs to - the two tabs of the profile picker.
    ///
    /// Avatars and frames share one definition type and one code path throughout the feature; this
    /// is what tells them apart at the three points that actually care: which catalog list to read,
    /// which PlayerData field to write, and which tab to render into.
    ///
    /// Named Avatar, not Icon, to match what the picker calls it on screen. The underlying
    /// PlayerData field is still ProfilePictureId - that predates this feature and is not worth a
    /// save migration to rename.
    /// </summary>
    public enum ProfileSlot
    {
        Avatar = 0,
        Frame = 1
    }
}
