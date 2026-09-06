namespace PTZControlServer;

public static class EndpointMappings
{
    public static void MapRestActions(RouteGroupBuilder api)
    {
        api.MapMethods("/camera/{slot:int}/zoom-absolute", ["POST", "PUT"], (int slot, string mode, int value, CameraApiService service) =>
            RunAction("zoom-absolute", slot, () => service.SetZoomAsync(slot, value, CameraApiService.ParseMode(mode), false)))
            .WithTags("Zoom").WithSummary("Set an absolute zoom value.");
        api.MapMethods("/camera/{slot:int}/zoom-relative", ["POST", "PUT"], (int slot, string mode, int value, CameraApiService service) =>
            RunAction("zoom-relative", slot, () => service.SetZoomAsync(slot, value, CameraApiService.ParseMode(mode), true)))
            .WithTags("Zoom").WithSummary("Change zoom by a relative value.");
        api.MapMethods("/camera/{slot:int}/move-absolute", ["POST", "PUT"], (int slot, string mode, int? pan, int? tilt, CameraApiService service) =>
            RunAction("move-absolute", slot, () => service.MoveAsync(slot, pan, tilt, CameraApiService.ParseMode(mode), false)))
            .WithTags("Move").WithSummary("Set absolute pan and/or tilt values.");
        api.MapMethods("/camera/{slot:int}/move-relative", ["POST", "PUT"], (int slot, string mode, int? pan, int? tilt, CameraApiService service) =>
            RunAction("move-relative", slot, () => service.MoveAsync(slot, pan, tilt, CameraApiService.ParseMode(mode), true)))
            .WithTags("Move").WithSummary("Change pan and/or tilt by relative values.");
        api.MapMethods("/camera/{slot:int}/restore-preset/{preset:int}", ["POST", "PUT"], (int slot, int preset, CameraApiService service) =>
            RunAction("restore-preset", slot, () => service.ExecuteAsync(slot, (backend, camera) => backend.RestorePreset(camera, CameraApiService.ValidatePreset(preset)))))
            .WithTags("Presets").WithSummary("Restore a preset position.");
        api.MapMethods("/camera/{slot:int}/save-preset/{preset:int}", ["POST", "PUT"], (int slot, int preset, CameraApiService service) =>
            RunAction("save-preset", slot, () => service.ExecuteAsync(slot, (backend, camera) => backend.SavePreset(camera, CameraApiService.ValidatePreset(preset)))))
            .WithTags("Presets").WithSummary("Save the current camera position as a preset.");
        api.MapMethods("/camera/{slot:int}/restore-home", ["POST", "PUT"], (int slot, string target, CameraApiService service) =>
            RunAction("restore-home", slot, () => service.ExecuteAsync(slot, (backend, camera) =>
            {
                var (zoom, move) = CameraApiService.ParseHomeTarget(target);
                backend.RestoreHome(camera, zoom, move);
            }))).WithTags("Restore").WithSummary("Restore the Logitech home position.");
        api.MapMethods("/camera/{slot:int}/restore-default", ["POST", "PUT"], (int slot, string target, CameraApiService service) =>
            RunAction("restore-default", slot, () => service.ExecuteAsync(slot, (backend, camera) =>
            {
                var (zoom, pan, tilt) = CameraApiService.ParseDefaultTarget(target);
                backend.RestoreDefault(camera, zoom, pan, tilt);
            }))).WithTags("Restore").WithSummary("Restore driver default values.");
    }

    public static void MapConvenienceActions(RouteGroupBuilder actions)
    {
        actions.MapGet("/zoom-absolute", (int slot, string mode, int value, CameraApiService service) =>
            RunAction("zoom-absolute", slot, () => service.SetZoomAsync(slot, value, CameraApiService.ParseMode(mode), false)));
        actions.MapGet("/zoom-relative", (int slot, string mode, int value, CameraApiService service) =>
            RunAction("zoom-relative", slot, () => service.SetZoomAsync(slot, value, CameraApiService.ParseMode(mode), true)));
        actions.MapGet("/move-absolute", (int slot, string mode, int? pan, int? tilt, CameraApiService service) =>
            RunAction("move-absolute", slot, () => service.MoveAsync(slot, pan, tilt, CameraApiService.ParseMode(mode), false)));
        actions.MapGet("/move-relative", (int slot, string mode, int? pan, int? tilt, CameraApiService service) =>
            RunAction("move-relative", slot, () => service.MoveAsync(slot, pan, tilt, CameraApiService.ParseMode(mode), true)));
        actions.MapGet("/restore-preset", (int slot, int preset, CameraApiService service) =>
            RunAction("restore-preset", slot, () => service.ExecuteAsync(slot, (backend, camera) => backend.RestorePreset(camera, CameraApiService.ValidatePreset(preset)))));
        actions.MapGet("/save-preset", (int slot, int preset, CameraApiService service) =>
            RunAction("save-preset", slot, () => service.ExecuteAsync(slot, (backend, camera) => backend.SavePreset(camera, CameraApiService.ValidatePreset(preset)))));
        actions.MapGet("/restore-home", (int slot, string target, CameraApiService service) =>
            RunAction("restore-home", slot, () => service.ExecuteAsync(slot, (backend, camera) =>
            {
                var (zoom, move) = CameraApiService.ParseHomeTarget(target);
                backend.RestoreHome(camera, zoom, move);
            })));
        actions.MapGet("/restore-default", (int slot, string target, CameraApiService service) =>
            RunAction("restore-default", slot, () => service.ExecuteAsync(slot, (backend, camera) =>
            {
                var (zoom, pan, tilt) = CameraApiService.ParseDefaultTarget(target);
                backend.RestoreDefault(camera, zoom, pan, tilt);
            })));
    }

    private static async Task<IResult> RunAction(string action, int slot, Func<Task> operation)
    {
        await operation();
        return Results.Ok(new ActionResultDto(true, action, slot, "OK"));
    }
}
