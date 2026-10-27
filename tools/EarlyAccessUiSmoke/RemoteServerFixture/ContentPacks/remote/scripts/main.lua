local readyCount = 0
local readyFrozen = false
local sceneName = ""
local churnQueries = 0
local readySnapshot = nil
local retainedPlayer = nil

Events.Client.Ready.Add(function(ev)
    readyCount = readyCount + 1
    readySnapshot = ev
    sceneName = ev.SceneName or ""
    local changed = pcall(function() ev.SceneName = "forbidden" end)
    readyFrozen = not changed and ev.SceneName == sceneName
    Output:Log("Remote smoke received frozen Client.Ready: " .. sceneName)
end)

Messages.On("GetState", function(message)
    if message.Command == "show" then
        assert(Output:ShowText("remote owner A\n<color=red>literal</color>", 10))
    elseif message.Command == "clear" or message.Command == "expire" then
        Output:ClearText()
    end
    if message.Command == "churn" then
        for index = 1, 8 do
            local observed = Game.LocalPlayer
            assert(observed ~= nil and not observed.IsDestroyed)
            assert(Game:GetPlayer(observed.PlayerId).Nickname == observed.Nickname)
            local position = Game:GetPosition(observed.PlayerId)
            assert(position ~= nil and position.X == position.X)
            churnQueries = churnQueries + 1
        end
    end
    local player = Game.LocalPlayer
    assert(player ~= nil and not player.IsDestroyed)
    local id = player.PlayerId
    local position = Game:GetPosition(id)
    if retainedPlayer == nil then retainedPlayer = player end
    assert(not retainedPlayer.IsDestroyed and retainedPlayer.PlayerId == id)
    assert(retainedPlayer.Nickname == player.Nickname)
    local queried = Game:GetPlayer(id)
    local first = Game:GetPlayerAt(0)
    local frozenStillValid = readySnapshot ~= nil and readySnapshot.SceneName == sceneName
    local accepted, reason = Messages.Send("State", {
        RequestId = message.RequestId,
        Command = message.Command,
        Enabled = true,
        ReadyCount = readyCount,
        ReadyFrozen = readyFrozen and frozenStillValid,
        SceneName = sceneName,
        Nickname = player.Nickname,
        PlayerId = id,
        PlayerCount = Game.PlayerCount,
        QueryMatches = queried ~= nil and queried.PlayerId == id and first ~= nil,
        X = position.X, Y = position.Y, Z = position.Z,
        ChurnQueries = churnQueries
    })
    assert(accepted, reason)
end)

-- Every subscription and function above must load before native/public Ready.
Output:Log("Remote smoke primary fully loaded.")
