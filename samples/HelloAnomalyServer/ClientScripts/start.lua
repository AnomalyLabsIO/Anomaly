local function ShowStatus()
    local player = Game.LocalPlayer
    local name = "Waiting for local player"
    if player ~= nil and not player.IsDestroyed then
        name = player.Nickname
    end
    Output:ShowText("Anomaly Lua ready\n" .. name .. "\nRound: " .. Game.RoundPhase, 10)
end

Output:Log("Script initialized; waiting for client readiness.")
Events.Client.Ready.Add(ShowStatus)
Events.Player.Added.Add(ShowStatus)

Messages.On("GetState", function(Message)
    Output:ShowText("Server script request " .. Message.RequestId, 5)
    local accepted, reason = Messages.Send("State", {
        RequestId = Message.RequestId,
        Enabled = true
    })
    if not accepted then
        Output:Log("State reply rejected: " .. reason)
    end
end)
