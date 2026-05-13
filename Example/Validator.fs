namespace Example

open System

module Validator =
    let isAllowed (now: DateTime) (date: DateTime) = now.Date <= date
