namespace ObjecTalk.Domain.Recognition
{
    /// <summary>
    /// Moves a relative position from a whole image into the coordinates of a crop taken from it.
    ///
    /// The registration screens need this because they show the same photograph two different
    /// ways: the placement screen shows the full frame, and the eye and mouth positions are
    /// recorded against it, while the naming screen shows only the centre square that actually
    /// gets registered. Drawing the recorded position straight onto the crop puts the face in the
    /// wrong place, and by more on the long axis of a portrait frame than on the short one.
    /// </summary>
    public static class CropRemap
    {
        /// <summary>
        /// Where a 0..1 position in the whole image falls within <paramref name="crop"/>, also as
        /// 0..1. A position outside the crop is clamped to its border rather than drawn off the
        /// picture - the object was framed inside the crop, so anything beyond it is a placement
        /// the registered photograph never contained.
        /// </summary>
        public static (float x, float y) ToCropSpace(float relativeX, float relativeY, in NormalizedRect crop)
        {
            return (
                Remap(relativeX, crop.OffsetX, crop.ScaleX),
                Remap(relativeY, crop.OffsetY, crop.ScaleY));
        }

        private static float Remap(float value, float offset, float scale)
        {
            if (scale <= 0f)
            {
                return 0.5f;
            }

            var mapped = (value - offset) / scale;
            return mapped <= 0f ? 0f : (mapped >= 1f ? 1f : mapped);
        }
    }
}
