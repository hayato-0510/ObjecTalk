# Android Object Recognition Optimization Design

## Goal

Replace the current Barracuda/MobileNetV2 classification pipeline with a responsive, resource-conscious registration-based object recognition system for Android 10+ devices. Existing recognition feature data does not need to remain compatible; users will register objects again with the new model.

## Scope and success criteria

- Target Android 10 (API 29) or later, ARM64, and devices with approximately 4 GB RAM or more.
- Preserve the product behavior of recognizing user-registered individual objects. General-purpose object detection and bounding boxes are out of scope.
- Register three views per object to improve robustness to angle, distance, and background changes.
- Target a median recognition-cycle time of 100 ms or less on representative hardware, without visible UI stalls.
- Run recognition adaptively at approximately two to five attempts per second and avoid steady-state managed allocations in the recognition loop.
- Complete a 30-minute device run without crashes or material progressive slowdown.

## Architecture

The system will separate camera-frame acquisition, image preprocessing, neural embedding extraction, candidate scoring, local-feature verification, temporal stabilization, and persistence. These components will communicate through narrow interfaces so the embedding model or a future region detector can be changed without rewriting the registration and presentation flows.

Unity Barracuda and the existing MobileNetV2 classifier output will be replaced with Unity Sentis 2.4.x, the released inference package for Unity 6. The embedding model will be a MobileNetV3-Small-class network with its classifier removed. It will accept a single 224 x 224 RGB image and emit an L2-normalized embedding. The shipped model will use FP16 weights. An INT8 variant may only replace it after Android device measurements demonstrate equal recognition quality and better end-to-end latency; INT8 is not a prerequisite for this delivery.

The runtime will select a GPU compute backend when compute shaders are supported. It will fall back to a CPU backend if compute is unavailable or GPU worker initialization fails. Inference will be scheduled across frames so a complete model execution does not block one UI frame.

## Image pipeline

Camera frames will remain GPU-resident until a CPU image is specifically needed for AKAZE. Preprocessing will center-crop the camera image to a square without distortion, resize it to 224 x 224, convert it to the model's RGB channel order and numeric range, and apply the exact mean and standard deviation recorded with the exported model.

Reusable render textures, tensors, CPU pixel buffers, and OpenCV matrices will be allocated outside the steady-state recognition loop. The system will never cast `WebCamTexture` to `Texture2D`. When local verification is required, it will explicitly copy the current frame into a reusable CPU-readable texture no larger than 320 x 320 and convert that texture to an OpenCV matrix.

## Registration flow

Registration will guide the user through three captures of the same object from different angles. Each capture will produce:

- one normalized neural embedding;
- AKAZE keypoints and descriptors computed from the bounded verification image;
- the image dimensions and preprocessing/model version needed to validate persisted data.

The three samples will be stored under one registered object identity. Registration will only complete after all three valid samples exist. A failed or featureless capture will be rejected with a retry message instead of saving a partial object.

## Recognition flow

1. Accept a new camera frame only when the camera is active, registered objects exist, and no prior inference is pending.
2. Preprocess the frame on the GPU and schedule Sentis inference across frames.
3. Compare the normalized result with every registered sample using cosine similarity.
4. Aggregate the three sample scores for each object using the highest score, with a configurable margin between the best and second-best objects to reject ambiguous matches.
5. Run AKAZE verification only for the best neural candidate and only after it passes the neural threshold.
6. Feed the verified result into a temporal stabilizer requiring repeated positive evidence to enter recognition and a configurable grace interval to leave recognition.
7. Publish recognition state changes only when the stabilized state changes.

The inspector-exposed similarity threshold, ambiguity margin, minimum local-match count, enter count, exit grace interval, and minimum/maximum recognition interval will be the sole runtime configuration values. Hard-coded duplicate thresholds will be removed.

## Adaptive workload control

The recognition scheduler will measure each completed cycle and adjust its next start interval within configured bounds of approximately 200 to 500 ms. It will not queue frames while inference is busy. AKAZE will run only after neural screening and on the bounded verification image. Camera and inference work will stop when the recognition panel is inactive, the application pauses, or camera permission is unavailable.

## Persistence and migration

Persisted recognition features will carry an explicit schema/model version. On startup, rows generated by the old MobileNetV2 pipeline or by a different embedding/preprocessing version will be invalidated. Because compatibility is not required, the application will remove only obsolete recognition-feature records, retain unrelated user/profile data, and tell the user that objects must be registered again.

Each registered object will own three sample records rather than one feature blob. Neural embeddings and AKAZE data will be serialized with dimensions and format metadata and validated before allocation or OpenCV reconstruction. Corrupt samples will be skipped and reported without preventing other objects from loading.

## Android configuration

- Minimum Android API level: 29.
- Target architecture: ARM64 only.
- Scripting backend: IL2CPP.
- Managed stripping: enabled at a level validated against Firebase, SQLite, OpenCV, and Sentis runtime behavior.
- Graphics APIs: Vulkan preferred, OpenGL ES 3 retained as a compatibility fallback.
- Camera permission denial, revocation, pause, and resume will produce explicit state transitions and user-facing Japanese messages.

The target SDK will follow the installed Unity/Android toolchain and current distribution requirements rather than being pinned to the project's existing API 28 value.

## Error handling and lifecycle

Model-load or GPU-worker initialization failure will trigger one CPU initialization attempt. If both fail, recognition will be disabled without crashing and the UI will show a Japanese error message. Camera and database failures will similarly disable only the affected operation.

All workers, tensors, render textures, textures, OpenCV matrices, keypoint/descriptor containers, and camera resources will have one documented owner. Owners will release resources idempotently during panel shutdown, application pause, and destruction. Pending inference results will be ignored after shutdown through a generation/cancellation token.

## Testing

Edit Mode tests will cover:

- aspect-preserving crop calculations;
- pixel normalization and L2 normalization;
- cosine similarity and three-sample aggregation;
- ambiguity-margin rejection;
- temporal enter/exit behavior;
- adaptive interval bounds;
- schema/model-version invalidation without deleting unrelated data;
- backend selection and GPU-to-CPU fallback policy;
- configuration propagation so inspector values, not constants, reach matching.

Play Mode tests will cover lifecycle and integration boundaries:

- camera/frame-source start, pause, resume, and stop;
- no overlapping inference requests;
- explicit camera-frame conversion rather than a `WebCamTexture`/`Texture2D` cast;
- worker and image-buffer release;
- registration completion only after three valid samples;
- user-facing failure states.

Device profiling will measure median and 95th-percentile recognition-cycle time, main-thread frame time, managed allocations, memory growth, backend selected, and thermal/performance behavior over 30 minutes. Recognition quality will be evaluated with multiple objects registered from three views and a held-out set varying angle, distance, lighting, and background. Thresholds will be calibrated from these results rather than asserted from desktop testing.

## Delivery boundaries

This work includes model export/import metadata, runtime migration, three-view registration, optimized recognition, automated tests, Android settings, and a device-test checklist. Training a custom dataset, detecting arbitrary object locations, cloud inference, and preserving old feature blobs are excluded. A future detector can supply cropped regions to the image-preprocessing interface without changing embedding storage or match aggregation.
