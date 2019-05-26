/// Login web part and functions for API web part request authorisation with JWT.
module Jernl.Auth

open Suave
open Suave.RequestErrors
open Jernl.Effects.DB
open BsonDocConverter
open Newtonsoft.Json

type LoginDetails =
    { email: string
      password: string }

type Login = { user: LoginDetails }

type Token = { token: string }

let unauthorized s = Suave.Response.response HTTP_401 s

let UNAUTHORIZED s = unauthorized (UTF8.bytes s)

let validatePassword passwordHash passedInPassword = 
    Jernl.Hash.Crypto.verify passwordHash passedInPassword
  
let loginWithCredentials dbClient (ctx: HttpContext) = async {
    let deserializeToLogin json = JsonConvert.DeserializeObject<Login>(json)
    let login = 
        ctx.request.rawForm 
        |> System.Text.Encoding.UTF8.GetString
        |> deserializeToLogin
  
    try
    let checkedPassword = getUser dbClient login.user.email
    match checkedPassword with
    | Some pass -> 
        let passHash = extractPasswordHash pass

        if not (validatePassword passHash login.user.password) then    
            return! failwithf "Could not authenticate %s" login.user.email

        let user : JsonWebToken.UserRights = { UserName = login.user.email }
        let token: Token = { token = JsonWebToken.encode user }

        return! Successful.OK (JsonConvert.SerializeObject token) ctx
    | _ -> 
        return! failwithf "Could not authenticate %s" login.user.email
    with
    | _ -> return! UNAUTHORIZED (sprintf "User '%s' can't be logged in." login.user.email) ctx
}

/// Invokes a function that produces the output for a web part if the HttpContext
/// contains a valid auth token. Use to authorise the expressions in your web part
/// code (e.g. WishList.getWishList).
let useToken ctx f = async {
    match ctx.request.header "Authorization" with
    | Choice1Of2 accesstoken when accesstoken.StartsWith "Token " -> 
        let jwt = accesstoken.Replace("Token ","")
        match JsonWebToken.isValid jwt with
        | None -> return! FORBIDDEN "Accessing this API is not allowed" ctx
        | Some token -> return! f token
    | _ -> return! BAD_REQUEST "Request doesn't contain a JSON Web Token" ctx
}